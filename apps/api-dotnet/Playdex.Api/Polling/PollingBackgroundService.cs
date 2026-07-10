using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Playdex.Api.Configuration;
using Playdex.Api.Metrics;
using Playdex.Api.Playnite;

namespace Playdex.Api.Polling;

/// <summary>
/// Background service that periodically polls external gaming platforms
/// (Steam, PSN, etc.) to detect what users are currently playing and
/// synchronises play sessions accordingly.
///
/// Cycle:
///   1. End timed-out Playnite sessions.
///   2. Load active platform credentials (excluding "playnite").
///   3. For each credential, call the appropriate provider client.
///   4. Upsert game, check ignore list, record poll result.
///   5. Sync play session (start / keep alive / end).
///
/// On failure, applies exponential backoff (base 5 s, max 10 min).
/// On HTTP 429, falls back 60 s.
/// </summary>
public sealed class PollingBackgroundService(
    IServiceProvider serviceProvider,
    PlaydexOptions options,
    MetricsCollector metrics,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    private const int BaseBackoffMs = 5_000;
    private const int MaxBackoffMs = 600_000;
    private const int RateLimitFallbackMs = 60_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Randomise initial delay to avoid thundering herd on startup.
        var initialDelayMs = Random.Shared.Next(0, 5001);
        logger.LogInformation("PollingBackgroundService will start after {Delay} ms", initialDelayMs);
        await Task.Delay(initialDelayMs, stoppingToken);

        var backoffMs = BaseBackoffMs;

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStart = Stopwatch.StartNew();
            logger.LogDebug("Starting poll cycle");

            try
            {
                using var scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
                var scopedServices = scope.ServiceProvider;

                var pollingRepo = scopedServices.GetRequiredService<IPollingRepository>();
                var playniteService = scopedServices.GetRequiredService<PlayniteService>();
                var playniteRepo = scopedServices.GetRequiredService<IPlayniteRepository>();
                var dataSource = scopedServices.GetRequiredService<NpgsqlDataSource>();
                var providerClients = scopedServices.GetServices<IProviderClient>()
                    .ToDictionary(c => c.PlatformKey, StringComparer.OrdinalIgnoreCase);

                // Step 1 — End timed-out Playnite sessions before checking
                // other platforms so stale Playnite sessions don't overlap.
                var endedSessions = await playniteService.EndTimedOutSessions(stoppingToken);
                if (endedSessions > 0)
                {
                    logger.LogInformation("Ended {Count} timed-out Playnite sessions", endedSessions);
                }

                // Step 2 — Load active credentials (everything except Playnite).
                var credentials = await pollingRepo.LoadActiveCredentials(stoppingToken);
                logger.LogDebug("Loaded {Count} active credentials", credentials.Count);

                // Step 3 — Process each credential.
                foreach (var active in credentials)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    await ProcessCredential(
                        active, pollingRepo, playniteRepo, dataSource,
                        providerClients, stoppingToken);
                }

                // Success → reset backoff.
                backoffMs = BaseBackoffMs;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Poll cycle failed");
                metrics.RecordPollFailure("all", DateTimeOffset.UtcNow);

                // Exponential backoff before next cycle attempt.
                logger.LogWarning("Backing off for {BackoffMs} ms", backoffMs);
                await Task.Delay(backoffMs, stoppingToken);
                backoffMs = (int)Math.Min(backoffMs * 2L, MaxBackoffMs);
                continue;
            }

            // Wait for the remainder of the poll interval (if any) before
            // starting the next cycle.
            var elapsed = cycleStart.ElapsedMilliseconds;
            metrics.RecordCycleDuration(elapsed);

            var delayMs = Math.Max(0, (options.PollIntervalSeconds * 1000L) - elapsed);
            await Task.Delay((int)delayMs, stoppingToken);
        }
    }

    private async Task ProcessCredential(
        ActiveCredential active,
        IPollingRepository pollingRepo,
        IPlayniteRepository playniteRepo,
        NpgsqlDataSource dataSource,
        Dictionary<string, IProviderClient> providerClients,
        CancellationToken ct)
    {
        var credential = active.Credential;
        var platformKey = active.PlatformKey;

        if (!providerClients.TryGetValue(platformKey, out var client))
        {
            logger.LogWarning("No IProviderClient registered for platform {Platform}", platformKey);
            return;
        }

        // ── Call the provider API ───────────────────────────────
        CurrentlyPlayingResult? providerResult;

        try
        {
            providerResult = await client.GetCurrentlyPlaying(credential, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            logger.LogWarning(
                "Rate limited (429) on {Platform} for credential {CredentialId}; " +
                "falling back {FallbackMs} ms",
                platformKey, credential.Id, RateLimitFallbackMs);

            metrics.RecordPollFailure(platformKey, DateTimeOffset.UtcNow);
            await Task.Delay(RateLimitFallbackMs, ct);
            return;
        }
        // Other exceptions bubble to the outer cycle handler for backoff.

        var polledAt = DateTimeOffset.UtcNow;

        // ── No game currently playing ───────────────────────────
        if (providerResult is null)
        {
            await EndAllSessions(playniteRepo, dataSource, credential.Id, polledAt, ct);
            await pollingRepo.RecordPollResult(credential.Id, null, polledAt, null, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            logger.LogDebug("Credential {CredentialId}: no game playing, ended sessions", credential.Id);
            return;
        }

        // ── Game detected — upsert and sync ─────────────────────
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        var canonicalKey = $"{platformKey}:{providerResult.ExternalId ?? ComputeFallbackId(providerResult.Title)}";
        var gameId = await playniteRepo.UpsertGame(
            conn, tx,
            providerResult.Title,
            providerResult.ExternalId,
            platformKey,
            canonicalKey,
            providerResult.CapsuleUrl,
            ct);

        var ignored = await pollingRepo.CheckIfGameIgnored(credential.UserId, gameId, ct);

        if (ignored)
        {
            logger.LogDebug(
                "Credential {CredentialId}: game {GameId} is ignored, ending sessions",
                credential.Id, gameId);

            await playniteRepo.EndActiveSessionsForCredential(conn, tx, credential.Id, polledAt, ct);
            await tx.CommitAsync(ct);

            await pollingRepo.RecordPollResult(credential.Id, gameId, polledAt, null, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            return;
        }

        await SyncPlaySession(conn, tx, playniteRepo, credential.Id, gameId, polledAt, ct);
        await tx.CommitAsync(ct);

        await pollingRepo.RecordPollResult(credential.Id, gameId, polledAt, null, ct);
        metrics.RecordPollSuccess(platformKey, polledAt);
        logger.LogDebug("Credential {CredentialId}: synced session for game {GameId}", credential.Id, gameId);
    }

    private static async Task SyncPlaySession(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        IPlayniteRepository playniteRepo,
        long credentialId,
        long gameId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var active = await playniteRepo.FetchActiveSessionForCredential(conn, tx, credentialId, ct);

        if (active is not null)
        {
            if (active.GameId == gameId)
            {
                // Same game still being played → keep alive.
                await playniteRepo.TouchActiveSession(conn, tx, active.Id, now, ct);
            }
            else
            {
                // Switched to a different game → end old session, start new.
                await playniteRepo.EndActiveSessionsForCredential(conn, tx, credentialId, now, ct);
                await playniteRepo.InsertActivePlaySession(conn, tx, credentialId, gameId, now, ct);
            }
        }
        else
        {
            // No active session → start a new one.
            await playniteRepo.InsertActivePlaySession(conn, tx, credentialId, gameId, now, ct);
        }
    }

    private static async Task EndAllSessions(
        IPlayniteRepository playniteRepo,
        NpgsqlDataSource dataSource,
        long credentialId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await playniteRepo.EndActiveSessionsForCredential(conn, null, credentialId, now, ct);
    }

    /// <summary>
    /// Deterministic fallback ID for titles that lack an external ID
    /// (e.g. when the provider returns only a title).
    /// </summary>
    private static string ComputeFallbackId(string title)
    {
        // Use a stable hash so the same title always maps to the same
        // canonical key slot.
        var hash = title.GetHashCode(StringComparison.Ordinal);
        return $"hash-{hash:X8}";
    }
}
