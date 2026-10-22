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
    ILogger<PollingBackgroundService> logger) : BackgroundService
{
    internal const string ActivitySourceName = "Playdex.Api.Polling";
    private static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private readonly CredentialBackoffTracker _backoff = new();

    [LoggerMessage(Level = LogLevel.Information, Message = "PollingBackgroundService will start after {Delay} ms")]
    private partial void LogWillStartAfter(int delay);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Starting poll cycle")]
    private partial void LogStartingPollCycle();

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

                    foreach (var active in credentials)
                    {
                        if (stoppingToken.IsCancellationRequested) break;
                        var now = DateTimeOffset.UtcNow;
                        if (!_backoff.IsEligible(active.Credential.Id, now, out var nextEligibleAt))
                        {
                            LogSkippingCredential(active.Credential.Id, nextEligibleAt);
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
                            LogCredentialFailed(ex, active.Credential.Id, active.PlatformKey, delay, ex.Message);
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
        var pollingRepo = services.GetRequiredService<PollingRepository>();
        var bus = services.GetRequiredService<EventBus>();
        var providerClients = services.GetServices<IProviderClient>()
            .ToDictionary(client => client.PlatformKey, StringComparer.OrdinalIgnoreCase);

        if (!providerClients.TryGetValue(platformKey, out var client))
        {
            LogNoProviderClient(platformKey);
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
