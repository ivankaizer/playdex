using Playdex.Web.Utilities;
using Xunit;

namespace Playdex.Web.Tests.Utilities;

public sealed class FormattersTests
{
    [Theory]
    [InlineData("ps5", "PS5")]
    [InlineData("psn", "PS5")]
    [InlineData("switch-1", "Nintendo Switch 1")]
    [InlineData("switch-2", "Nintendo Switch 2")]
    [InlineData("xbox-series-xs", "Xbox Series X|S")]
    [InlineData("other", "other")]
    public void FormatPlatformLabel_UsesTheReactMappingOrFallsBack(string platform, string expected)
    {
        Assert.Equal(expected, Formatters.FormatPlatformLabel(platform));
    }

    [Theory]
    [InlineData("psn", "PlayStation")]
    [InlineData("steam", "Steam")]
    [InlineData("other", "other")]
    public void FormatProviderName_UsesTheReactMappingOrFallsBack(string platform, string expected)
    {
        Assert.Equal(expected, Formatters.FormatProviderName(platform));
    }

    [Fact]
    public void FormatSessionPlatformLabel_PrefersTheMappedPlatform()
    {
        Assert.Equal("Nintendo Switch 2", Formatters.FormatSessionPlatformLabel("steam", "switch-2"));
        Assert.Equal("Steam", Formatters.FormatSessionPlatformLabel("steam", null));
    }

    [Fact]
    public void FormatStatsPlatformLabel_UsesTheSharedPlatformMapping()
    {
        Assert.Equal("Xbox Series X|S", Formatters.FormatStatsPlatformLabel("xbox-series-xs"));
    }

    [Theory]
    [InlineData("igdb", "IGDB")]
    [InlineData("local", "Local")]
    [InlineData(null, "Local")]
    public void FormatManualMappingType_PreservesTheNullFallback(string? source, string expected)
    {
        Assert.Equal(expected, Formatters.FormatManualMappingType(source));
    }

    [Fact]
    public void FormatManualInputLabel_UsesTheRawInputWhenNoGameIsMapped()
    {
        Assert.Equal("elden rnig", Formatters.FormatManualInputLabel("elden rnig", null, null, null));
    }

    [Fact]
    public void FormatManualInputLabel_FormatsMappedAndMissingPlatformValues()
    {
        Assert.Equal(
            "PS5 · IGDB · Elden Ring",
            Formatters.FormatManualInputLabel("ignored", "Elden Ring", "psn", "igdb"));
        Assert.Equal(
            "Unknown platform · Local · Mystery Game",
            Formatters.FormatManualInputLabel("ignored", "Mystery Game", null, null));
    }

    [Fact]
    public void FormatEntryResultLabel_OnlyAddsAPlatformForLocalResults()
    {
        Assert.Equal("Hades · Steam", Formatters.FormatEntryResultLabel("local", "Hades", "steam"));
        Assert.Equal("Hades · Unknown platform", Formatters.FormatEntryResultLabel("local", "Hades", null));
        Assert.Equal("Hades", Formatters.FormatEntryResultLabel("igdb", "Hades", "steam"));
    }

    [Fact]
    public void DateAndTimeLabels_UseEnUsBrowserStyleOutput()
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
    [InlineData(-0.1, DurationFormat.Short, "0m")]
    [InlineData(0, DurationFormat.Short, "0m")]
    [InlineData(59.999, DurationFormat.Long, "0 min")]
    [InlineData(60, DurationFormat.Short, "1m")]
    [InlineData(3599, DurationFormat.Long, "59 min")]
    [InlineData(3600, DurationFormat.Short, "1h 00m")]
    [InlineData(3660, DurationFormat.Long, "1h 1m")]
    public void FormatDuration_MatchesReactBoundaries(double seconds, DurationFormat format, string expected)
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
    public void FormatHours_MatchesJavascriptRoundingAndTrailingZeroBehavior(
        double seconds,
        int decimals,
        string expected)
    {
        Assert.Equal(expected, Formatters.FormatHours(seconds, decimals));
    }

    [Fact]
    public void FormatUtcDateInput_UsesTheUtcCalendarDate()
    {
        var date = new DateTimeOffset(2026, 7, 13, 0, 30, 0, TimeSpan.FromHours(2));

        Assert.Equal("2026-07-12", Formatters.FormatUtcDateInput(date));
    }
}
