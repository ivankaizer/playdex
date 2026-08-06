using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Playdex.Api.Shared.Common;
using Playdex.Api.Modules.Metrics;

namespace Playdex.Api.Modules.Polling;

public sealed class PollingBackgroundService(
    IServiceProvider serviceProvider,
    PlaydexOptions options,
    MetricsCollector metrics,
    EventBus bus,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    private const int BaseBackoffMs = 5_000;
    private const int MaxBackoffMs = 600_000;
    private const int RateLimitFallbackMs = 60_000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("starting polling");
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
                var providerClients = scopedServices.GetServices<IProviderClient>()
                    .ToDictionary(c => c.PlatformKey, StringComparer.OrdinalIgnoreCase);

                var credentials = await pollingRepo.LoadActiveCredentials(stoppingToken);
                logger.LogDebug("Loaded {Count} active credentials", credentials.Count);

                foreach (var active in credentials)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        await ProcessCredential(
                            active, pollingRepo, providerClients, stoppingToken);
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
            await pollingRepo.RecordPollResult(credential.Id, null, polledAt, null, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            logger.LogDebug("Credential {CredentialId}: provider unreachable", credential.Id);
            return;
        }

        if (pollResult.Game is null)
        {
            await pollingRepo.RecordPollResult(credential.Id, null, polledAt, pollResult.RawPayload, ct);
            metrics.RecordPollSuccess(platformKey, polledAt);
            logger.LogDebug("Credential {CredentialId}: no game playing", credential.Id);
            return;
        }

        var game = pollResult.Game;
        var externalId = game.ExternalId ?? ComputeFallbackId(game.Title);

        await bus.Publish(new GameDetected(credential.Id, externalId, game.Title, platformKey), ct);

        await pollingRepo.RecordPollResult(credential.Id, null, polledAt, pollResult.RawPayload, ct);
        metrics.RecordPollSuccess(platformKey, polledAt);
        logger.LogDebug("Credential {CredentialId}: published game {ExternalId}", credential.Id, externalId);
    }

    private static string ComputeFallbackId(string title)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(title));
        return $"hash-{Convert.ToHexString(hash)[..16]}";
    }
}
