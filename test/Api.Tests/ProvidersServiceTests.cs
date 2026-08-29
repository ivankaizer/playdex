using Api.Modules.Providers;
using Api.Infrastructure.Http;
using Api.Infrastructure.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class ProvidersServiceTests
{
    [Theory]
    [InlineData(" steam ", " steam-key ", " 76561198000000000 ", "steam", IntegrationType.Steam, "steam-key", "76561198000000000")]
    [InlineData("psn", " npsso-token ", null, "psn", IntegrationType.Psn, "npsso-token", null)]
    [InlineData("xbox", " openxbl-key ", " 2533274817903789 ", "xbox", IntegrationType.Xbox, "openxbl-key", "2533274817903789")]
    public void ValidateCreationAcceptsSupportedPlatforms(
        string platform,
        string apiKey,
        string? accountId,
        string expectedPlatform,
        IntegrationType expectedType,
        string expectedApiKey,
        string? expectedAccountId)
    {
        var creation = ProvidersService.ValidateCreation(
            new CreateProviderRequest(platform, apiKey, accountId));

        Assert.Equal(expectedPlatform, creation.Platform);
        Assert.Equal(expectedType, creation.Type);
        Assert.Equal(expectedApiKey, creation.ApiKey);
        Assert.Equal(expectedAccountId, creation.AccountId);
    }

    [Theory]
    [InlineData("steam", null, "76561198000000000", "Steam Web API key is required.")]
    [InlineData("steam", "steam-key", null, "Steam ID is required.")]
    [InlineData("psn", null, null, "PlayStation NPSSO token is required.")]
    [InlineData("xbox", null, null, "OpenXBL API key is required.")]
    [InlineData("xbox", "openxbl-key", "not-a-xuid", "Xbox XUID must contain digits only.")]
    [InlineData("api", "secret", null, "Platform must be steam, psn, or xbox.")]
    [InlineData(null, "secret", null, "Platform must be steam, psn, or xbox.")]
    public void ValidateCreationRejectsInvalidRequests(
        string? platform,
        string? apiKey,
        string? accountId,
        string message)
    {
        var exception = Assert.Throws<AppProblemException>(() =>
            ProvidersService.ValidateCreation(new CreateProviderRequest(platform, apiKey, accountId)));

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal(message, exception.Message);
    }

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
