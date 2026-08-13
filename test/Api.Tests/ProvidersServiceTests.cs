using Api.Modules.Providers;
using Xunit;

namespace Api.Tests;

public sealed class ProvidersServiceTests
{
    [Fact]
    public void ToResponseIncludesReadOnlyLocalApiKey()
    {
        var provider = new ProviderStatusDto(
            1,
            "api",
            "local",
            "active",
            "plx_secret",
            ApiKeyLocked: true);

        var response = ProvidersService.ToResponse(provider);

        Assert.Equal("plx_secret", response.ApiKey);
        Assert.True(response.ApiKeyLocked);
    }

    [Fact]
    public void ToResponseStillHidesAdministratorManagedProviderKey()
    {
        var provider = new ProviderStatusDto(
            2,
            "steam",
            "account",
            "active",
            "managed-secret",
            ApiKeyLocked: true);

        var response = ProvidersService.ToResponse(provider);

        Assert.Null(response.ApiKey);
        Assert.True(response.ApiKeyLocked);
    }
}
