using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Api.Infrastructure.Common;
using Playdex.Persistence;
using Api.Modules.ApiCapture;
using Api.Modules.Metrics;

namespace Api.Modules.Polling;

public sealed partial class PollingBackgroundService(
    IServiceProvider serviceProvider,
    IOptions<PollingOptions> pollingOptions,
    MetricsCollector metrics,
    PollingLease lease,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    internal const string ActivitySourceName = "Playdex.Api.Polling";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private readonly CredentialBackoffTracker _backoff = new();

    [LoggerMessage(Level = LogLevel.Information, Message = "PollingBackgroundService will start after {Delay} ms")]
    private partial void LogWillStartAfter(int delay);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting poll cycle")]
    private partial void LogStartingPollCycle();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping poll cycle: another instance is polling")]
    private partial void LogLeaseHeldElsewhere();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Loaded {Count} active credentials")]
    private partial void LogLoadedCredentials(int count);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping credential {CredentialId} until {NextEligibleAt}")]
    private partial void LogSkippingCredential(long credentialId, DateTimeOffset nextEligibleAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Credential {CredentialId} ({Platform}) failed; retrying in {Delay}: {Message}")]
    private partial void LogCredentialFailed(Exception exception, long credentialId, string platform, TimeSpan delay, string message);

    [LoggerMessage(Level = LogLevel.Error, Message = "Poll cycle failed")]
    private partial void LogPollCycleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No IProviderClient registered for platform {Platform}")]
    private partial void LogNoProviderClient(string platform);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Credential {CredentialId}: successful poll resolved game {GameId}")]
    private partial void LogSuccessfulPoll(long credentialId, long? gameId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Game metadata enrichment failed for credential {CredentialId}, game {GameId}")]
    private partial void LogMetadataEnrichmentFailed(Exception exception, long credentialId, long gameId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Event handler failed for {EventType}")]
    private partial void LogEventHandlerFailed(Exception exception, string eventType);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialDelayMs = Random.Shared.Next(0, 5001);
        LogWillStartAfter(initialDelayMs);
        await Task.Delay(initialDelayMs, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStart = Stopwatch.StartNew();
            using (var activity = ActivitySource.StartActivity("polling.cycle", ActivityKind.Internal))
            {
                LogStartingPollCycle();

                try
                {
                    await using var heldLease = await lease.TryAcquire(stoppingToken);
                    if (heldLease is null)
                    {
                        activity?.SetTag("polling.skipped", true);
                        LogLeaseHeldElsewhere();
                    }
                    else
                    {
                        await RunCycle(activity, stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    LogPollCycleFailed(ex);
                    metrics.RecordPollFailure("all", DateTimeOffset.UtcNow);

                    activity?.Stop();
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }
            }

            var elapsed = cycleStart.ElapsedMilliseconds;
            metrics.RecordCycleDuration(elapsed);

            var delayMs = Math.Max(0, (pollingOptions.Value.IntervalSeconds * 1000L) - elapsed);
            await Task.Delay((int)delayMs, stoppingToken);
        }
    }

    private async Task RunCycle(Activity? activity, CancellationToken stoppingToken)
    {
        await EndTimedOutApiSessions(serviceProvider, stoppingToken);

        List<ActiveCredential> credentials;
        using (var scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            credentials = await scope.ServiceProvider
                .GetRequiredService<PollingRepository>()
                .LoadActiveCredentials(stoppingToken);
        }
        activity?.SetTag("polling.credentials.count", credentials.Count);
        LogLoadedCredentials(credentials.Count);
        _backoff.Prune(credentials.Select(row => row.Credential.Id));

        var now = DateTimeOffset.UtcNow;
        var due = new List<ActiveCredential>(credentials.Count);
        foreach (var active in credentials)
        {
            if (_backoff.IsEligible(active.Credential.Id, now, out var nextEligibleAt))
            {
                due.Add(active);
            }
            else
            {
                LogSkippingCredential(active.Credential.Id, nextEligibleAt);
            }
        }

        await PollAll(due, stoppingToken);
    }

    /// <summary>
    /// Bounded parallelism: one provider taking its full HTTP timeout used to delay every credential behind it.
    /// Each credential has its own scope, and its database work is serialized by its own advisory lock.
    /// </summary>
    internal async Task PollAll(IReadOnlyList<ActiveCredential> due, CancellationToken stoppingToken)
    {
        await Parallel.ForEachAsync(
            due,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, pollingOptions.Value.MaxConcurrentPolls),
                CancellationToken = stoppingToken,
            },
            async (active, ct) => await PollCredential(active, ct));
    }

    /// <summary>Polls one credential; a failure is recorded and never escapes, except shutdown cancellation.</summary>
    internal async Task PollCredential(ActiveCredential active, CancellationToken stoppingToken)
    {
        try
        {
            using var credentialScope = serviceProvider
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope();
            await ProcessCredential(active, credentialScope.ServiceProvider, stoppingToken);
            _backoff.Reset(active.Credential.Id);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a provider failure: no failure metric, backoff or notification.
            throw;
        }
        catch (Exception ex)
        {
            var retryAfter = ex is ProviderPollException providerFailure
                ? providerFailure.RetryAfter
                : null;
            var failure = _backoff.RecordFailure(
                active.Credential.Id, DateTimeOffset.UtcNow, retryAfter);
            LogCredentialFailed(ex, active.Credential.Id, active.PlatformKey, failure.Delay, ex.Message);
            metrics.RecordPollFailure(active.PlatformKey, DateTimeOffset.UtcNow);
            if (failure.IsFirst)
            {
                // Notify when a working integration starts failing, not on every retry: an expired token would
                // otherwise send a message every backoff interval (~144 a day).
                using var notificationScope = serviceProvider
                    .GetRequiredService<IServiceScopeFactory>()
                    .CreateScope();
                await SafePublish(
                    notificationScope.ServiceProvider.GetRequiredService<EventBus>(),
                    new PollFailed(
                        active.Credential.Id,
                        active.PlatformKey,
                        ex.Message,
                        failure.Delay),
                    stoppingToken);
            }
        }
    }

    internal static async Task<int> EndTimedOutApiSessions(
        IServiceProvider serviceProvider,
        CancellationToken ct)
    {
        using var scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider
            .GetRequiredService<IApiSessionTimeoutCleaner>()
            .EndTimedOutSessions(ct);
    }

    private async Task ProcessCredential(
        ActiveCredential active,
        IServiceProvider services,
        CancellationToken ct)
    {
        var credential = active.Credential;
        var platformKey = active.PlatformKey;
        var providerClients = services.GetServices<IProviderClient>()
            .ToDictionary(client => client.PlatformKey, StringComparer.OrdinalIgnoreCase);

        if (!providerClients.TryGetValue(platformKey, out var client))
        {
            LogNoProviderClient(platformKey);
            return;
        }

        var pollResult = await client.GetCurrentlyPlaying(credential, ct);
        var pollingRepo = services.GetRequiredService<PollingRepository>();
        var bus = services.GetRequiredService<EventBus>();

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
        await EnrichGameIfNeeded(client, credential, pollResult.Game, outcome, pollingRepo, ct);
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
        LogSuccessfulPoll(credential.Id, outcome.GameId);
    }

    private async Task EnrichGameIfNeeded(
        IProviderClient client,
        IntegrationCredentials credential,
        CurrentlyPlayingResult? detectedGame,
        SuccessfulPollOutcome outcome,
        PollingRepository repository,
        CancellationToken ct)
    {
        if (outcome.Ignored ||
            !outcome.NeedsCapsule ||
            outcome.GameId is not { } gameId ||
            string.IsNullOrWhiteSpace(detectedGame?.ExternalId) ||
            client is not IProviderGameMetadataClient metadataClient)
        {
            return;
        }

        try
        {
            var capsuleUrl = await metadataClient.GetCapsuleUrl(
                credential,
                detectedGame.ExternalId,
                ct);
            if (!string.IsNullOrWhiteSpace(capsuleUrl))
                await repository.UpdateGameCapsule(gameId, capsuleUrl, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogMetadataEnrichmentFailed(ex, credential.Id, gameId);
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
            LogEventHandlerFailed(ex, typeof(T).Name);
        }
    }
}

/// <param name="Delay">How long the credential is skipped.</param>
/// <param name="IsFirst">The credential was healthy until this failure.</param>
public sealed record CredentialFailure(TimeSpan Delay, bool IsFirst);

/// <summary>Per-credential failure backoff. Thread-safe: credentials are polled in parallel.</summary>
public sealed class CredentialBackoffTracker
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(10);
    private readonly Dictionary<long, CredentialBackoffState> _states = [];
    private readonly Lock _gate = new();

    public bool IsEligible(long credentialId, DateTimeOffset now, out DateTimeOffset nextEligibleAt)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(credentialId, out var state) && state.NextEligibleAt > now)
            {
                nextEligibleAt = state.NextEligibleAt;
                return false;
            }
        }

        nextEligibleAt = now;
        return true;
    }

    public CredentialFailure RecordFailure(
        long credentialId,
        DateTimeOffset now,
        TimeSpan? retryAfter = null)
    {
        lock (_gate)
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
            return new CredentialFailure(delay, IsFirst: failures == 1);
        }
    }

    public void Reset(long credentialId)
    {
        lock (_gate)
        {
            _states.Remove(credentialId);
        }
    }

    public void Prune(IEnumerable<long> activeCredentialIds)
    {
        var active = activeCredentialIds.ToHashSet();
        lock (_gate)
        {
            foreach (var credentialId in _states.Keys.Where(id => !active.Contains(id)).ToArray())
            {
                _states.Remove(credentialId);
            }
        }
    }
}

public sealed record CredentialBackoffState(int Failures, DateTimeOffset NextEligibleAt);
