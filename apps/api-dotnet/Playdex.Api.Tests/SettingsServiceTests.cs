using Playdex.Api.Http;
using Playdex.Api.Settings;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class SettingsServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1441)]
    [InlineData(int.MaxValue)]
    public void Validation_AcceptsEveryNonNegativeInteger(int value)
    {
        Assert.Equal(value, SettingsService.ValidateIgnoreShortSessionsMinutes(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1)]
    public void Validation_RejectsMissingOrNegativeValues(int? value)
    {
        var problem = Assert.Throws<AppProblem>(
            () => SettingsService.ValidateIgnoreShortSessionsMinutes(value));

        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(
            "ignoreShortSessionsMinutes must be a non-negative integer.",
            problem.Message);
    }
}
