using System.Diagnostics.Metrics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace Api.Modules.Polling;

/// <summary>Counters and timings of the poller: <c>playdex.polls</c> and <c>playdex.poll.cycles</c>.</summary>
public static class PollingMetrics
{
    public const string MeterName = "Playdex.Api.Polling";

    internal static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> _polls = Meter.CreateCounter<long>(
        "playdex.polls",
        description: "Polls of an integration by outcome: success, failure or auth_failure (the credential was rejected).");

    private static readonly Counter<long> _cycles = Meter.CreateCounter<long>(
        "playdex.poll.cycles",
        description: "Poll cycles by outcome: completed, failed, or skipped while another instance polls.");

    private static readonly Histogram<double> _cycleDuration = Meter.CreateHistogram(
        "playdex.poll.cycle.duration",
        unit: "s",
        description: "How long a completed poll cycle took.",
        advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120] });

    public static void RecordPoll(ActiveCredential active, string outcome) =>
        _polls.Add(
            1,
            new("platform", active.PlatformKey),
            new("integration_id", Id(active.Credential.Id)),
            new("user_id", Id(active.Credential.UserId)),
            new("outcome", outcome));

    public static void RecordCycle(string outcome, TimeSpan? duration = null)
    {
        _cycles.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        if (duration is { } elapsed)
        {
            _cycleDuration.Record(elapsed.TotalSeconds);
        }
    }

    internal static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One integration as monitoring sees it.</summary>
/// <param name="Active">False when the user turned it off or Playdex paused it.</param>
/// <param name="Failures">Failed polls since the last successful one.</param>
public sealed record IntegrationHealth(
    long IntegrationId,
    long UserId,
    string Platform,
    bool Active,
    DateTimeOffset? PausedAt,
    int Failures,
    DateTimeOffset? FailingSince,
    DateTimeOffset? LastPolledAt)
{
    public static readonly string[] States = ["healthy", "failing", "paused", "disabled", "never_polled"];

    public string State => this switch
    {
        { Active: false, PausedAt: not null } => "paused",
        { Active: false } => "disabled",
        { Failures: > 0 } => "failing",
        { LastPolledAt: null } => "never_polled",
        _ => "healthy",
    };
}

/// <summary>
/// The state of every integration, as gauges: <c>playdex.integration.state</c> (1 for the state it is in, 0 for the
/// others, so a change of state never leaves a stale series behind),
/// <c>playdex.integration.since_success</c> and <c>playdex.integration.failing_for</c>. The instance that polls
/// refreshes it after each cycle; an instance that has stopped polling stops reporting it once it is stale, so only
/// the current poller's view is exported.
/// </summary>
public sealed class IntegrationHealthGauges
{
    private readonly TimeProvider _clock;
    private readonly IOptions<PollingOptions> _options;
    private Snapshot? _snapshot;

    public IntegrationHealthGauges(TimeProvider clock, IOptions<PollingOptions> options)
    {
        _clock = clock;
        _options = options;
        PollingMetrics.Meter.CreateObservableGauge(
            "playdex.integration.state",
            ObserveState,
            description: "1 for the state each integration is in, 0 for the others: healthy, failing, paused, disabled or never_polled.");
        PollingMetrics.Meter.CreateObservableGauge(
            "playdex.integration.since_success",
            ObserveSinceSuccess,
            unit: "s",
            description: "Seconds since the integration was last polled successfully.");
        PollingMetrics.Meter.CreateObservableGauge(
            "playdex.integration.failing_for",
            ObserveFailingFor,
            unit: "s",
            description: "Seconds an active integration has been failing; 0 when it works.");
    }

    public void Update(IReadOnlyList<IntegrationHealth> integrations) =>
        _snapshot = new Snapshot(integrations, _clock.GetUtcNow());

    private IEnumerable<Measurement<int>> ObserveState() =>
        Current().SelectMany(integration => IntegrationHealth.States.Select(state => new Measurement<int>(
            state == integration.State ? 1 : 0,
            Tags(integration, ("state", state)))));

    private IEnumerable<Measurement<double>> ObserveSinceSuccess()
    {
        var now = _clock.GetUtcNow();
        return Current()
            .Where(integration => integration.LastPolledAt is not null)
            .Select(integration => new Measurement<double>(
                Math.Max(0, (now - integration.LastPolledAt!.Value).TotalSeconds),
                Tags(integration)));
    }

    private IEnumerable<Measurement<double>> ObserveFailingFor()
    {
        var now = _clock.GetUtcNow();
        return Current()
            .Where(integration => integration.Active)
            .Select(integration => new Measurement<double>(
                integration.FailingSince is { } since ? Math.Max(0, (now - since).TotalSeconds) : 0,
                Tags(integration)));
    }

    private IReadOnlyList<IntegrationHealth> Current()
    {
        var snapshot = _snapshot;
        var staleAfter = TimeSpan.FromSeconds(Math.Max(1, _options.Value.IntervalSeconds) * 3);
        return snapshot is not null && _clock.GetUtcNow() - snapshot.TakenAt <= staleAfter
            ? snapshot.Integrations
            : [];
    }

    private static KeyValuePair<string, object?>[] Tags(IntegrationHealth integration, params (string Key, string Value)[] extra) =>
    [
        new("platform", integration.Platform),
        new("integration_id", PollingMetrics.Id(integration.IntegrationId)),
        new("user_id", PollingMetrics.Id(integration.UserId)),
        .. extra.Select(tag => new KeyValuePair<string, object?>(tag.Key, tag.Value)),
    ];

    private sealed record Snapshot(IReadOnlyList<IntegrationHealth> Integrations, DateTimeOffset TakenAt);
}
