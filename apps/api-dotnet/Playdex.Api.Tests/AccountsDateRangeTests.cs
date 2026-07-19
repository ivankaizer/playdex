using Playdex.Api.Modules.Accounts;
using Playdex.Api.Shared.Http;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class AccountsDateRangeTests
{
    [Fact]
    public void ParseOptional_DateOnlyBoundsUseUtcAndIncludeTheWholeToDay()
    {
        var range = AccountDateRange.ParseOptional("2026-07-13", "2026-07-13");

        Assert.Equal(new DateTimeOffset(2026, 7, 13, 0, 0, 0, TimeSpan.Zero), range.From);
        Assert.Equal(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero), range.ToExclusive);
        Assert.Equal(TimeSpan.Zero, range.From?.Offset);
        Assert.Equal(TimeSpan.Zero, range.ToExclusive?.Offset);
    }

    [Fact]
    public void ParseOptional_ExplicitOffsetsAreNormalizedToUtc()
    {
        var range = AccountDateRange.ParseOptional(
            "2026-07-13T01:30:00+02:00",
            "2026-07-13T23:30:00-02:00");

        Assert.Equal(new DateTimeOffset(2026, 7, 12, 23, 30, 0, TimeSpan.Zero), range.From);
        Assert.Equal(new DateTimeOffset(2026, 7, 15, 0, 0, 0, TimeSpan.Zero), range.ToExclusive);
    }

    [Theory]
    [InlineData("not-a-date", null)]
    [InlineData(null, "not-a-date")]
    public void ParseOptional_InvalidSuppliedBoundThrowsExactValidationError(string? from, string? to)
    {
        var exception = Assert.Throws<AppProblem>(() => AccountDateRange.ParseOptional(from, to));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("from and to must be valid dates.", exception.Message);
    }

    [Fact]
    public void ParseOptional_TimeOnlyBoundThrowsExactValidationError()
    {
        var exception = Assert.Throws<AppProblem>(() => AccountDateRange.ParseOptional("12:34", null));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("from and to must be valid dates.", exception.Message);
    }

    [Fact]
    public void ParseOptional_UnrepresentableInclusiveToThrowsExactValidationError()
    {
        var exception = Assert.Throws<AppProblem>(
            () => AccountDateRange.ParseOptional(null, "9999-12-31"));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("from and to must be valid dates.", exception.Message);
    }

    [Theory]
    [InlineData(null, "2026-07-13")]
    [InlineData("2026-07-13", null)]
    [InlineData("", "2026-07-13")]
    [InlineData("2026-07-13", "")]
    public void ParseRequired_MissingBoundThrowsExactRequiredError(string? from, string? to)
    {
        var exception = Assert.Throws<AppProblem>(() => AccountDateRange.ParseRequired(from, to));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("from and to are required.", exception.Message);
    }

    [Fact]
    public void ParseRequired_InvalidBoundThrowsExactValidationError()
    {
        var exception = Assert.Throws<AppProblem>(
            () => AccountDateRange.ParseRequired("2026-07-13", "not-a-date"));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("from and to must be valid dates.", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Parse_ReversedBoundsThrowExactOrderingError(bool required)
    {
        var exception = Assert.Throws<AppProblem>(() => required
            ? AccountDateRange.ParseRequired("2026-07-14", "2026-07-13")
            : AccountDateRange.ParseOptional("2026-07-14", "2026-07-13"));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("from must be before to.", exception.Message);
    }
}
