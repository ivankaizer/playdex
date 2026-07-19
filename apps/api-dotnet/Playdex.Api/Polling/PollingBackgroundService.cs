using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Playdex.Api.Configuration;
using Playdex.Api.Data;
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

                var pollingRepo = scopedServices.GetRequiredService<PollingRepository>();
                var playniteService = scopedServices.GetRequiredService<PlayniteService>();
                var playniteRepo = scopedServices.GetRequiredService<PlayniteRepository>();
                var db = scopedServices.GetRequiredService<PlaydexDbContext>();
                var providerClients = scopedServices.GetServices<IProviderClient>()
                    .ToDictionary(c => c.PlatformKey, StringComparer.OrdinalIgnoreCase);

                var endedSessions = await playniteService.EndTimedOutSessions(stoppingToken);
                if (endedSessions > 0)
                {
                    logger.LogInformation("Ended {Count} timed-out Playnite sessions", endedSessions);
                }

                var credentials = await pollingRepo.LoadActiveCredentials(stoppingToken);
                logger.LogDebug("Loaded {Count} active credentials", credentials.Count);

                foreach (var active in credentials)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        await ProcessCredential(
                            active, pollingRepo, playniteRepo, db,
                            providerClients, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Credential {CredentialId} ({Platform}) failed, continuing: {Message}",
                            active.Credential.Id, active.PlatformKey, ex.Message);
                        metrics.RecordPollFailure(active.PlatformKey, DateTimeOffset.UtcNow);
                    }
                }

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

                logger.LogWarning("Backing off for {BackoffMs} ms", backoffMs);
                await Task.Delay(backoffMs, stoppingToken);
                backoffMs = (int)Math.Min(backoffMs * 2L, MaxBackoffMs);
                continue;
            }

            var elapsed = cycleStart.ElapsedMilliseconds;
            metrics.RecordCycleDuration(elapsed);

            var delayMs = Math.Max(0, (options.PollIntervalSeconds * 1000L) - elapsed);
            await Task.Delay((int)delayMs, stoppingToken);
        }
    }

    private async Task ProcessCredential(
        ActiveCredential active,
        PollingRepository pollingRepo,
        PlayniteRepository playniteRepo,
        PlaydexDbContext db,
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

        ProviderPollResult? pollResult;

        try
        {
            pollResult = await client.GetCurrentlyPlaying(credential, ct);
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

        var polledAt = DateTimeOffset.UtcNow;

        if (pollResult is null)
        {
            await EndAllSessions(playniteRepo, db, credential.Id, polledAt, ct);
            await pollingRepo.RecordPollResult(credential.Id, null, polledAt, null, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            logger.LogDebug("Credential {CredentialId}: provider unreachable, ended sessions", credential.Id);
            return;
        }

        if (pollResult.Game is null)
        {
            await EndAllSessions(playniteRepo, db, credential.Id, polledAt, ct);
            await pollingRepo.RecordPollResult(credential.Id, null, polledAt, pollResult.RawPayload, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            logger.LogDebug("Credential {CredentialId}: no game playing, ended sessions", credential.Id);
            return;
        }

        var game = pollResult.Game;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var canonicalKey = $"{platformKey}:{game.ExternalId ?? ComputeFallbackId(game.Title)}";
        var gameId = await playniteRepo.UpsertGame(
            game.Title,
            game.ExternalId,
            platformKey,
            canonicalKey,
            game.CapsuleUrl,
            ct);

        var ignored = await pollingRepo.CheckIfGameIgnored(credential.UserId, gameId, ct);

        if (ignored)
        {
            logger.LogDebug(
                "Credential {CredentialId}: game {GameId} is ignored, ending sessions",
                credential.Id, gameId);

            await playniteRepo.EndActiveSessionsForCredential(credential.Id, polledAt, ct);
            await tx.CommitAsync(ct);

            await pollingRepo.RecordPollResult(credential.Id, gameId, polledAt, pollResult.RawPayload, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            return;
        }

        await SyncPlaySession(db, playniteRepo, credential.Id, gameId, polledAt, ct);
        await tx.CommitAsync(ct);

        await pollingRepo.RecordPollResult(credential.Id, gameId, polledAt, pollResult.RawPayload, ct);
        metrics.RecordPollSuccess(platformKey, polledAt);
        logger.LogDebug("Credential {CredentialId}: synced session for game {GameId}", credential.Id, gameId);
    }

    private static async Task SyncPlaySession(
        PlaydexDbContext db,
        PlayniteRepository playniteRepo,
        long credentialId,
        long gameId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var active = await playniteRepo.FetchActiveSessionForCredential(credentialId, ct);

        if (active is not null)
        {
            if (active.GameId == gameId)
            {
                await playniteRepo.TouchActiveSession(active.Id, now, ct);
            }
            else
            {
                await playniteRepo.EndActiveSessionsForCredential(credentialId, now, ct);
                await playniteRepo.InsertActivePlaySession(credentialId, gameId, now, ct);
            }
        }
        else
        {
            await playniteRepo.InsertActivePlaySession(credentialId, gameId, now, ct);
        }
    }

    private static async Task EndAllSessions(
        PlayniteRepository playniteRepo,
        PlaydexDbContext db,
        long credentialId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await playniteRepo.EndActiveSessionsForCredential(credentialId, now, ct);
    }

    private static string ComputeFallbackId(string title)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return $"hash-{Convert.ToHexString(hash)[..16]}";
    }
}
