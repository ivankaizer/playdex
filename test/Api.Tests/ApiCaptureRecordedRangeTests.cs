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
}
