using Api.Modules.ApiCapture;
using Xunit;

namespace Api.Tests;

public sealed class ApiCaptureRecordedRangeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WithoutAStartTheSessionEndsNow()
    {
        var (startedAt, endedAt) = ApiCaptureService.RecordedRange(Now, 3600, startedAt: null);

        Assert.Equal(Now.AddHours(-1), startedAt);
        Assert.Equal(Now, endedAt);
    }

    [Fact]
    public void AQueuedSessionKeepsItsRealStartInUtc()
    {
        // Played last night on a PC two hours ahead of UTC, sent from the offline queue this evening.
        var started = new DateTimeOffset(2026, 10, 21, 23, 30, 0, TimeSpan.FromHours(2));

        var (startedAt, endedAt) = ApiCaptureService.RecordedRange(Now, 5400, started);

        Assert.Equal(started, startedAt);
        Assert.Equal(TimeSpan.Zero, startedAt.Offset);
        Assert.Equal(started.AddMinutes(90), endedAt);
        Assert.Equal(TimeSpan.Zero, endedAt.Offset);
    }

    [Fact]
    public void AStartThatWouldEndInTheFutureEndsNow()
    {
        // A client clock five minutes ahead of the server's.
        var (startedAt, endedAt) = ApiCaptureService.RecordedRange(Now, 600, Now.AddMinutes(-5));

        Assert.Equal(Now.AddMinutes(-10), startedAt);
        Assert.Equal(Now, endedAt);
    }

    [Fact]
    public void TimeTheServerHasNotGotIsRecordedWhole()
    {
        var pieces = ApiCaptureService.Uncovered(Now.AddHours(-1), Now, [(Now.AddHours(-3), Now.AddHours(-2))]);

        Assert.Equal([(Now.AddHours(-1), Now)], pieces);
    }

    [Fact]
    public void ASessionSentAgainRecordsNothing()
    {
        Assert.Empty(ApiCaptureService.Uncovered(Now.AddHours(-1), Now, [(Now.AddHours(-1), Now)]));
    }

    [Fact]
    public void PlayAfterTheServersTimeoutIsRecordedAfterTheSessionItClosed()
    {
        // Online from 17:00, last ping 17:20, closed by the server at 17:23; the client sends 17:23..18:00 (its own idea
        // of the close), but the server got one more ping at 17:21 and closed at 17:24.
        var pieces = ApiCaptureService.Uncovered(Now.AddMinutes(-37), Now, [(Now.AddHours(-1), Now.AddMinutes(-36))]);

        Assert.Equal([(Now.AddMinutes(-36), Now)], pieces);
    }

    [Fact]
    public void AGapBetweenTwoSessionsIsRecordedAndTheOngoingOneCoversItsTime()
    {
        var pieces = ApiCaptureService.Uncovered(Now.AddHours(-2), Now,
        [
            (Now.AddMinutes(-10), null),
            (Now.AddHours(-3), Now.AddMinutes(-90)),
        ]);

        Assert.Equal([(Now.AddMinutes(-90), Now.AddMinutes(-10))], pieces);
    }

    [Fact]
    public void PartsUnderASecondAreLeftOut()
    {
        var pieces = ApiCaptureService.Uncovered(Now.AddMinutes(-10), Now,
        [
            (Now.AddMinutes(-10).AddMilliseconds(500), Now.AddMilliseconds(-300)),
        ]);

        Assert.Empty(pieces);
    }
}
