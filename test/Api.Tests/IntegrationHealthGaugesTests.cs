using System.Diagnostics.Metrics;
using Api.Modules.Polling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Api.Tests;

public sealed class IntegrationHealthGaugesTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, false, 0, true, "healthy")]
    [InlineData(true, false, 3, true, "failing")]
    [InlineData(false, true, 10, true, "paused")]
    [InlineData(false, false, 0, true, "disabled")]
    [InlineData(true, false, 0, false, "never_polled")]
    public void StateFollowsActivityFailuresAndPolls(bool active, bool paused, int failures, bool polled, string expected)
    {
        var health = new IntegrationHealth(
            1, 1, "steam", active, paused ? _now : null, failures, failures > 0 ? _now : null, polled ? _now : null);

        Assert.Equal(expected, health.State);
    }

    [Fact]
    public void EveryStateIsExportedOnceAndAStaleSnapshotNotAtAll()
    {
        var clock = new ManualClock(_now);
        var gauges = new IntegrationHealthGauges(clock, Options.Create(new PollingOptions { IntervalSeconds = 60 }));
        gauges.Update([new IntegrationHealth(
            7_001_001, 2, "psn", true, null, 2, _now.AddMinutes(-40), _now.AddMinutes(-41))]);

        var current = Observe();
        Assert.Equal(IntegrationHealth.States.Length, current.State.Count);
        Assert.Equal(1, current.State["failing"]);
        Assert.Equal(0, current.State["healthy"]);
        Assert.Equal(40 * 60, current.FailingFor);
        Assert.Equal(41 * 60, current.SinceSuccess);

        // Three intervals without a refresh: another instance polls now, or none does.
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Empty(Observe().State);
    }

    /// <summary>What the gauges report now for integration 7001001, which no other test uses.</summary>
    private static (Dictionary<string, int> State, double? FailingFor, double? SinceSuccess) Observe()
    {
        var state = new Dictionary<string, int>();
        double? failingFor = null;
        double? sinceSuccess = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == PollingMetrics.MeterName &&
                instrument.Name.StartsWith("playdex.integration.", StringComparison.Ordinal))
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<int>((_, value, tags, _) =>
        {
            if (Tag(tags, "integration_id") == "7001001")
            {
                state[Tag(tags, "state")!] = value;
            }
        });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
        {
            if (Tag(tags, "integration_id") != "7001001")
            {
                return;
            }

            if (instrument.Name == "playdex.integration.failing_for")
            {
                failingFor = value;
            }
            else
            {
                sinceSuccess = value;
            }
        });
        listener.Start();
        listener.RecordObservableInstruments();
        return (state, failingFor, sinceSuccess);
    }

    private static string? Tag(ReadOnlySpan<KeyValuePair<string, object?>> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key == key)
            {
                return tag.Value as string;
            }
        }

        return null;
    }

    private sealed class ManualClock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
