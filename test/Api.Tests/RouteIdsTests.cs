using System.Globalization;
using Api.Infrastructure.Http;
using Xunit;

namespace Api.Tests;

public sealed class RouteIdsTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("42", 42)]
    [InlineData("9223372036854775807", long.MaxValue)]
    public void PositiveAsciiIntegersParse(string value, long expected) =>
        Assert.Equal(expected, RouteIds.Parse(value, "id"));

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("5 ")]
    [InlineData("1,000")]
    [InlineData("1e3")]
    [InlineData("٥")]                    // Arabic-Indic digit five
    [InlineData("9223372036854775808")] // long.MaxValue + 1
    [InlineData("abc")]
    [InlineData("")]
    public void AnythingElseIsAValidationProblemNamingTheParameter(string value)
    {
        var error = Assert.Throws<AppProblemException>(() => RouteIds.Parse(value, "providerId"));

        Assert.Equal(400, error.StatusCode);
        Assert.Equal("providerId must be a positive integer.", error.Message);
    }

    [Fact]
    public void ParsingDoesNotDependOnTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose negative sign is not "-" (U+2212 MINUS SIGN in Swedish).
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            Assert.Equal(7, RouteIds.Parse("7", "id"));
            Assert.Throws<AppProblemException>(() => RouteIds.Parse("−7", "id"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
