using Web.Infrastructure.Common;
using Xunit;

namespace Web.Tests.Utilities;

public sealed class FormattersTests
{
    [Theory]
    [InlineData("ps5", "PS5")]
    [InlineData("psn", "PS5")]
    [InlineData("switch-1", "Nintendo Switch 1")]
    [InlineData("switch-2", "Nintendo Switch 2")]
    [InlineData("xbox-series-xs", "Xbox Series X|S")]
    [InlineData("other", "other")]
    public void FormatPlatformLabelUsesTheReactMappingOrFallsBack(string platform, string expected)
    {
        Assert.Equal(expected, Formatters.FormatPlatformLabel(platform));
    }

    [Theory]
    [InlineData("psn", "PlayStation")]
    [InlineData("steam", "Steam")]
    [InlineData("other", "other")]
    public void FormatProviderNameUsesTheReactMappingOrFallsBack(string platform, string expected)
    {
        Assert.Equal(expected, Formatters.FormatProviderName(platform));
    }

    [Fact]
    public void FormatSessionPlatformLabelPrefersTheMappedPlatform()
    {
        Assert.Equal("Nintendo Switch 2", Formatters.FormatSessionPlatformLabel("steam", "switch-2"));
        Assert.Equal("Steam", Formatters.FormatSessionPlatformLabel("steam", null));
    }

    [Fact]
    public void FormatStatsPlatformLabelUsesTheSharedPlatformMapping()
    {
        Assert.Equal("Xbox Series X|S", Formatters.FormatStatsPlatformLabel("xbox-series-xs"));
    }

    [Fact]
    public void DateAndTimeLabelsUseEnUsBrowserStyleOutput()
    {
        var localDate = new DateTimeOffset(
            2026,
            7,
            13,
            13,
            5,
            0,
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 7, 13)));

        Assert.Equal("Jul 13, 2026", Formatters.FormatDateLabel(localDate));
        Assert.Equal("1:05 PM", Formatters.FormatTimeLabel(localDate));
        Assert.Equal("7/13/2026, 1:05:00 PM", Formatters.FormatLocalDateTime(localDate));
    }

    [Theory]
    [InlineData(-0.1, DurationFormat.Compact, "0m")]
    [InlineData(0, DurationFormat.Compact, "0m")]
    [InlineData(59.999, DurationFormat.Full, "0 min")]
    [InlineData(60, DurationFormat.Compact, "1m")]
    [InlineData(3599, DurationFormat.Full, "59 min")]
    [InlineData(3600, DurationFormat.Compact, "1h 00m")]
    [InlineData(3660, DurationFormat.Full, "1h 1m")]
    public void FormatDurationMatchesReactBoundaries(double seconds, DurationFormat format, string expected)
    {
        Assert.Equal(expected, Formatters.FormatDuration(seconds, format));
    }

    [Theory]
    [InlineData(1800, 0, "1h")]
    [InlineData(-1800, 0, "0h")]
    [InlineData(4500, 1, "1.3h")]
    [InlineData(9180, 1, "2.5h")]
    [InlineData(8100, 1, "2.3h")]
    [InlineData(-9180, 1, "-2.5h")]
    [InlineData(-8100, 1, "-2.3h")]
    [InlineData(3600, 1, "1h")]
    [InlineData(double.NaN, 1, "0h")]
    [InlineData(double.PositiveInfinity, 1, "0h")]
    [InlineData(double.NegativeInfinity, 1, "0h")]
    public void FormatHoursMatchesJavascriptRoundingAndTrailingZeroBehavior(
        double seconds,
        int decimals,
        string expected)
    {
        Assert.Equal(expected, Formatters.FormatHours(seconds, decimals));
    }

    [Fact]
    public void FormatUtcDateInputUsesTheUtcCalendarDate()
    {
        var date = new DateTimeOffset(2026, 7, 13, 0, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-07-12", Formatters.FormatUtcDateInput(date));
    }
}
