using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Playdex.Api.Shared.Common;
using Playdex.Api.Modules.Metrics;

namespace Playdex.Api.Modules.Polling;

public sealed class PollingBackgroundService(
    IServiceProvider serviceProvider,
    PlaydexOptions options,
    MetricsCollector metrics,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    private readonly CredentialBackoffTracker _backoff = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("starting polling");
        var initialDelayMs = Random.Shared.Next(0, 5001);
        logger.LogInformation("PollingBackgroundService will start after {Delay} ms", initialDelayMs);
        await Task.Delay(initialDelayMs, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStart = Stopwatch.StartNew();
            logger.LogDebug("Starting poll cycle");

            try
            {
                await CleanupTimedOutPlayniteSessions(stoppingToken);
                List<ActiveCredential> credentials;
                using (var scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope())
                {
                    credentials = await scope.ServiceProvider
                        .GetRequiredService<PollingRepository>()
                        .LoadActiveCredentials(stoppingToken);
                }
                logger.LogDebug("Loaded {Count} active credentials", credentials.Count);
                _backoff.Prune(credentials.Select(row => row.Credential.Id));

                foreach (var active in credentials)
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    var now = DateTimeOffset.UtcNow;
                    if (!_backoff.IsEligible(active.Credential.Id, now, out var nextEligibleAt))
                    {
                        logger.LogDebug(
                            "Skipping credential {CredentialId} until {NextEligibleAt}",
                            active.Credential.Id,
                            nextEligibleAt);
                        continue;
                    }

                    try
                    {
                        using var credentialScope = serviceProvider
                            .GetRequiredService<IServiceScopeFactory>()
                            .CreateScope();
                        await ProcessCredential(active, credentialScope.ServiceProvider, stoppingToken);
                        _backoff.Reset(active.Credential.Id);
                    }
                    catch (Exception ex)
                    {
                        var retryAfter = ex is ProviderPollException providerFailure
                            ? providerFailure.RetryAfter
                            : null;
                        var delay = _backoff.RecordFailure(
                            active.Credential.Id, DateTimeOffset.UtcNow, retryAfter);
                        logger.LogWarning(ex,
                            "Credential {CredentialId} ({Platform}) failed; retrying in {Delay}: {Message}",
                            active.Credential.Id, active.PlatformKey, delay, ex.Message);
                        metrics.RecordPollFailure(active.PlatformKey, DateTimeOffset.UtcNow);
                        using var notificationScope = serviceProvider
                            .GetRequiredService<IServiceScopeFactory>()
                            .CreateScope();
                        await SafePublish(
                            notificationScope.ServiceProvider.GetRequiredService<EventBus>(),
                            new PollFailed(
                                active.Credential.Id,
                                active.PlatformKey,
                                ex.Message,
                                delay),
                            stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Poll cycle failed");
                metrics.RecordPollFailure("all", DateTimeOffset.UtcNow);

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
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
        IServiceProvider services,
        CancellationToken ct)
    {
        var credential = active.Credential;
        var platformKey = active.PlatformKey;
        var pollingRepo = services.GetRequiredService<PollingRepository>();
        var bus = services.GetRequiredService<EventBus>();
        var providerClients = services.GetServices<IProviderClient>()
            .ToDictionary(client => client.PlatformKey, StringComparer.OrdinalIgnoreCase);

        if (!providerClients.TryGetValue(platformKey, out var client))
        {
            logger.LogWarning("No IProviderClient registered for platform {Platform}", platformKey);
            return;
        }

        var pollResult = await client.GetCurrentlyPlaying(credential, ct);

        var polledAt = DateTimeOffset.UtcNow;

        if (pollResult is null)
        {
            throw new ProviderPollException("Provider did not return a successful response.");
        }

        var outcome = await pollingRepo.ApplySuccessfulPoll(
            credential.Id,
            pollResult.Game,
            polledAt,
            pollResult.RawPayload,
            ct);
        foreach (var transition in outcome.Transitions.Where(row => row.Notify))
        {
            if (transition.Kind == SessionTransitionKind.Started)
            {
                await SafePublish(bus, new SessionStarted(
                    transition.SessionId,
                    transition.CredentialId,
                    transition.GameId,
                    transition.StartedAt), ct);
            }
            else
            {
                await SafePublish(bus, new SessionEnded(
                    transition.SessionId,
                    transition.CredentialId,
                    transition.GameId,
                    transition.StartedAt,
                    transition.EndedAt!.Value), ct);
            }
        }
        metrics.RecordPollSuccess(platformKey, polledAt);
        logger.LogDebug(
            "Credential {CredentialId}: successful poll resolved game {GameId}",
            credential.Id,
            outcome.GameId);
    }

    private async Task CleanupTimedOutPlayniteSessions(CancellationToken ct)
    {
        try
        {
            using var scope = serviceProvider
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope();
            var transitions = await scope.ServiceProvider
                .GetRequiredService<PollingRepository>()
                .EndTimedOutPlayniteSessions(
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(3),
                    ct);
            var bus = scope.ServiceProvider.GetRequiredService<EventBus>();
            foreach (var transition in transitions)
            {
                await SafePublish(bus, new SessionEnded(
                    transition.SessionId,
                    transition.CredentialId,
                    transition.GameId,
                    transition.StartedAt,
                    transition.EndedAt!.Value), ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Playnite timeout cleanup failed");
        }
    }

    private async Task SafePublish<T>(EventBus bus, T @event, CancellationToken ct)
    {
        try
        {
            await bus.Publish(@event, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Event handler failed for {EventType}", typeof(T).Name);
        }
    }
}

public sealed class CredentialBackoffTracker
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(10);
    private readonly Dictionary<long, CredentialBackoffState> _states = [];

    public bool IsEligible(long credentialId, DateTimeOffset now, out DateTimeOffset nextEligibleAt)
    {
        if (_states.TryGetValue(credentialId, out var state) && state.NextEligibleAt > now)
        {
            nextEligibleAt = state.NextEligibleAt;
            return false;
        }
        nextEligibleAt = now;
        return true;
    }

    public TimeSpan RecordFailure(
        long credentialId,
        DateTimeOffset now,
        TimeSpan? retryAfter = null)
    {
        var failures = _states.TryGetValue(credentialId, out var current)
            ? current.Failures + 1
            : 1;
        var exponent = Math.Min(failures - 1, 20);
        var exponentialMs = Math.Min(
            BaseDelay.TotalMilliseconds * Math.Pow(2, exponent),
            MaximumDelay.TotalMilliseconds);
        var delay = TimeSpan.FromMilliseconds(Math.Max(
            exponentialMs,
            retryAfter?.TotalMilliseconds ?? 0));
        _states[credentialId] = new CredentialBackoffState(failures, now + delay);
        return delay;
    }

    public void Reset(long credentialId) => _states.Remove(credentialId);

    public void Prune(IEnumerable<long> activeCredentialIds)
    {
        var active = activeCredentialIds.ToHashSet();
        foreach (var credentialId in _states.Keys.Where(id => !active.Contains(id)).ToArray())
        {
            _states.Remove(credentialId);
        }
    }
}

public sealed record CredentialBackoffState(int Failures, DateTimeOffset NextEligibleAt);
