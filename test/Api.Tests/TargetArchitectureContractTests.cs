using Api.Infrastructure.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class TargetArchitectureContractTests
{
    [Fact]
    public void PlaySessionOwnsRequiredDimensionsAndOptionalIntegration()
    {
        var properties = typeof(PlaySession).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.Contains(nameof(PlaySession.UserId), properties);
        Assert.Contains(nameof(PlaySession.GameId), properties);
        Assert.Contains(nameof(PlaySession.PlatformId), properties);
        Assert.Contains(nameof(PlaySession.CaptureMethod), properties);
        Assert.Contains(nameof(PlaySession.IntegrationId), properties);
        Assert.DoesNotContain("PlatformCredentialId", properties);
    }

    [Fact]
    public void GameHasNoPlatformOrExternalIdentifierColumns()
    {
        var properties = typeof(Game).GetProperties().Select(p => p.Name).ToHashSet();
        Assert.DoesNotContain("PlatformId", properties);
        Assert.DoesNotContain("ExternalId", properties);
        Assert.DoesNotContain("CanonicalKey", properties);
    }

    [Fact]
    public void DomainEnumsMatchTargetContract()
    {
        Assert.Equal(["Steam", "Psn", "Xbox", "Igdb"], Enum.GetNames<GameExternalIdentityType>());
        Assert.Equal(["Steam", "Psn", "Xbox", "Api"], Enum.GetNames<IntegrationType>());
        Assert.Equal(["Polling", "Api", "Manual"], Enum.GetNames<CaptureMethod>());
    }
}
