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
    CapsuleLookupBackoff capsuleLookups,
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    internal const string ActivitySourceName = "Playdex.Api.Polling";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not update the backoff of credential {CredentialId}")]
    private partial void LogBackoffUpdateFailed(Exception exception, long credentialId);

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

        var now = DateTimeOffset.UtcNow;
        var due = new List<ActiveCredential>(credentials.Count);
        foreach (var active in credentials)
        {
            if (active.NextPollAt is { } nextPollAt && nextPollAt > now)
            {
                LogSkippingCredential(active.Credential.Id, nextPollAt);
            }
            else
            {
                due.Add(active);
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
        DateTimeOffset? deferUntil;
        try
        {
            using var credentialScope = serviceProvider
                .GetRequiredService<IServiceScopeFactory>()
                .CreateScope();
            deferUntil = await ProcessCredential(active, credentialScope.ServiceProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a provider failure: no failure metric, backoff or notification.
            throw;
        }
        catch (Exception ex)
        {
            await RecordFailure(active, ex, stoppingToken);
            return;
        }

        await RecordSuccess(active, deferUntil, stoppingToken);
    }

    private async Task RecordSuccess(ActiveCredential active, DateTimeOffset? deferUntil, CancellationToken ct)
    {
        if (deferUntil is null && active.NextPollAt is null)
        {
            // Healthy before and after: nothing to write.
            return;
        }

        try
        {
            using var scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
            var backoff = scope.ServiceProvider.GetRequiredService<ICredentialBackoff>();
            if (deferUntil is { } until)
            {
                await backoff.Defer(active.Credential.Id, until, ct);
            }
            else
            {
                await backoff.Reset(active.Credential.Id, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogBackoffUpdateFailed(ex, active.Credential.Id);
        }
    }

    private async Task RecordFailure(ActiveCredential active, Exception exception, CancellationToken ct)
    {
        metrics.RecordPollFailure(active.PlatformKey, DateTimeOffset.UtcNow);
        var providerFailure = exception as ProviderPollException;
        using var scope = serviceProvider.GetRequiredService<IServiceScopeFactory>().CreateScope();
        CredentialFailure failure;
        try
        {
            failure = await scope.ServiceProvider.GetRequiredService<ICredentialBackoff>().RecordFailure(
                active.Credential.Id,
                DateTimeOffset.UtcNow,
                providerFailure?.RetryAfter,
                providerFailure?.IsAuthenticationFailure == true,
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogBackoffUpdateFailed(ex, active.Credential.Id);
            LogCredentialFailed(exception, active.Credential.Id, active.PlatformKey, TimeSpan.Zero, exception.Message);
            return;
        }

        LogCredentialFailed(exception, active.Credential.Id, active.PlatformKey, failure.Delay, exception.Message);
        if (failure.IsFirst)
        {
            // Notify when a working integration starts failing, not on every retry: an expired token would
            // otherwise send a message every backoff interval (~144 a day). The count is shared by every instance,
            // so only one of them sees the first failure.
            await SafePublish(
                scope.ServiceProvider.GetRequiredService<EventBus>(),
                new PollFailed(
                    active.Credential.Id,
                    active.PlatformKey,
                    exception.Message,
                    failure.Delay),
                ct);
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

    /// <returns>When the provider asked to be left alone (a rate-limited metadata lookup), until when.</returns>
    private async Task<DateTimeOffset?> ProcessCredential(
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
            return null;
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
        var deferUntil = await EnrichGameIfNeeded(client, credential, pollResult.Game, outcome, pollingRepo, ct);
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
        return deferUntil;
    }

    private async Task<DateTimeOffset?> EnrichGameIfNeeded(
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
            return null;
        }

        // A game without a capsule used to be looked up again on every poll for as long as it was played.
        var now = DateTimeOffset.UtcNow;
        if (!capsuleLookups.IsDue(gameId, now))
        {
            return null;
        }

        try
        {
            var capsuleUrl = await metadataClient.GetCapsuleUrl(
                credential,
                detectedGame.ExternalId,
                ct);
            if (string.IsNullOrWhiteSpace(capsuleUrl))
            {
                capsuleLookups.RecordMiss(gameId, now);
                return null;
            }

            await repository.UpdateGameCapsule(gameId, capsuleUrl, ct);
            capsuleLookups.Forget(gameId);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogMetadataEnrichmentFailed(ex, credential.Id, gameId);
            var retryAfter = (ex as ProviderPollException)?.RetryAfter;
            capsuleLookups.RecordFailure(gameId, now, retryAfter);
            // The metadata endpoint shares the account's quota with presence: honour its Retry-After there too.
            return retryAfter is { } delay ? now + delay : null;
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
