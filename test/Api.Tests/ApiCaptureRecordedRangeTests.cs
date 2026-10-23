using Api.Modules.ApiCapture;
using Xunit;

namespace Api.Tests;

public sealed class ApiCaptureRecordedRangeTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutAStartTheSessionEndsNow()
    {
        var (startedAt, endedAt) = ApiCaptureService.RecordedRange(_now, 3600, startedAt: null);

        Assert.Equal(_now.AddHours(-1), startedAt);
        Assert.Equal(_now, endedAt);
    }

    [Fact]
    public void AQueuedSessionKeepsItsRealStartInUtc()
    {
        // Played last night on a PC two hours ahead of UTC, sent from the offline queue this evening.
        var started = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.FromHours(2));

        var (startedAt, endedAt) = ApiCaptureService.RecordedRange(_now, 5400, started);

        Assert.Equal(started, startedAt);
        Assert.Equal(TimeSpan.Zero, startedAt.Offset);
        Assert.Equal(started.AddMinutes(90), endedAt);
        Assert.Equal(TimeSpan.Zero, endedAt.Offset);
    }

    [Fact]
    public void AStartThatWouldEndInTheFutureEndsNow()
    {
        // A client clock five minutes ahead of the server's.
        var (startedAt, endedAt) = ApiCaptureService.RecordedRange(_now, 600, _now.AddMinutes(-5));

        Assert.Equal(_now.AddMinutes(-10), startedAt);
        Assert.Equal(_now, endedAt);
    }

    [Fact]
    public void TimeTheServerHasNotGotIsRecordedWhole()
    {
        var pieces = ApiCaptureService.Uncovered(_now.AddHours(-1), _now, [(_now.AddHours(-3), _now.AddHours(-2))]);

        Assert.Equal([(_now.AddHours(-1), _now)], pieces);
    }

    [Fact]
    public void ASessionSentAgainRecordsNothing()
    {
        Assert.Empty(ApiCaptureService.Uncovered(_now.AddHours(-1), _now, [(_now.AddHours(-1), _now)]));
    }

    [Fact]
    public void PlayAfterTheServersTimeoutIsRecordedAfterTheSessionItClosed()
    {
        // Online from 17:00, last ping 17:20, closed by the server at 17:23; the client sends 17:23..18:00 (its own idea
        // of the close), but the server got one more ping at 17:21 and closed at 17:24.
        var pieces = ApiCaptureService.Uncovered(_now.AddMinutes(-37), _now, [(_now.AddHours(-1), _now.AddMinutes(-36))]);

        Assert.Equal([(_now.AddMinutes(-36), _now)], pieces);
    }

    [Fact]
    public void AGapBetweenTwoSessionsIsRecordedAndTheOngoingOneCoversItsTime()
    {
        var pieces = ApiCaptureService.Uncovered(_now.AddHours(-2), _now,
        [
            (_now.AddMinutes(-10), null),
            (_now.AddHours(-3), _now.AddMinutes(-90)),
        ]);

        Assert.Equal([(_now.AddMinutes(-90), _now.AddMinutes(-10))], pieces);
    }

    [Fact]
    public void PartsUnderASecondAreLeftOut()
    {
        var pieces = ApiCaptureService.Uncovered(_now.AddMinutes(-10), _now,
        [
            (_now.AddMinutes(-10).AddMilliseconds(500), _now.AddMilliseconds(-300)),
        ]);

        Assert.Empty(pieces);
    }
}
