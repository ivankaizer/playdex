using Api.Modules.Manual.Drafts;
using Xunit;

namespace Api.Tests;

public sealed class DraftRulesTests
{
    [Theory]
    [InlineData(null, 60)]
    [InlineData("2026-02-30", 60)]
    [InlineData("2026-09-09", 0)]
    [InlineData("2026-09-09", -1)]
    [InlineData("9999-12-31", 2147483647)]
    public void IncompleteOrOverflowingDatesCannotBeAccepted(string? date, int minutes)
    {
        Assert.False(DraftRules.TryGetTimes(date, minutes, out _, out _));
    }

    [Fact]
    public void HistoricalTimesUseUtcMidnightAndReviewedDuration()
    {
        Assert.True(DraftRules.TryGetTimes("2026-09-09", 90, out var start, out var end));
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 0, 0, 0, TimeSpan.Zero), start);
        Assert.Equal(new DateTimeOffset(2026, 9, 9, 1, 30, 0, TimeSpan.Zero), end);
    }
}
