using Api.Modules.Polling;
using Xunit;

namespace Api.Tests;

public sealed class CredentialBackoffTrackerTests
{
    [Fact]
    public void FailureState_IsIndependentHonorsRetryAfterAndResetsPerCredential()
    {
        var tracker = new CredentialBackoffTracker();
        var now = DateTimeOffset.Parse("2026-08-06T12:00:00Z");

        Assert.Equal(TimeSpan.FromSeconds(5), tracker.RecordFailure(10, now));
        Assert.Equal(TimeSpan.FromSeconds(10), tracker.RecordFailure(10, now));
        Assert.Equal(TimeSpan.FromMinutes(2),
            tracker.RecordFailure(20, now, TimeSpan.FromMinutes(2)));

        Assert.False(tracker.IsEligible(10, now.AddSeconds(9), out _));
        Assert.False(tracker.IsEligible(20, now.AddMinutes(1), out _));
        Assert.True(tracker.IsEligible(30, now, out _));

        tracker.Reset(10);

        Assert.True(tracker.IsEligible(10, now, out _));
        Assert.False(tracker.IsEligible(20, now, out _));
    }
}
