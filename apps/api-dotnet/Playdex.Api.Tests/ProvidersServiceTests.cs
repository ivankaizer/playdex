using Playdex.Api.Modules.Providers;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class ProvidersServiceTests
{
    [Fact]
    public void GeneratedPlayniteKeys_HaveTheExpectedPrefixAnd128BitsOfEntropy()
    {
        var keys = Enumerable.Range(0, 100)
            .Select(_ => ProvidersService.GenerateApiKey())
            .ToArray();

        Assert.All(keys, key => Assert.Matches("^plx_[0-9a-f]{32}$", key));
        Assert.Equal(keys.Length, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("steam")]
    [InlineData("psn")]
    [InlineData("manual")]
    public void Response_HidesApiKeysForEveryProviderExceptPlaynite(string platform)
    {
        var response = ProvidersService.ToResponse(
            new ProviderStatusDto(7, platform, "account", "active", "secret"));

        Assert.Null(response.ApiKey);
    }

    [Fact]
    public void Response_PreservesThePlayniteApiKey()
    {
        var response = ProvidersService.ToResponse(
            new ProviderStatusDto(7, "playnite", "local", "active", "plx_key"));

        Assert.Equal("plx_key", response.ApiKey);
    }
}
