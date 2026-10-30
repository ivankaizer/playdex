using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class TargetArchitectureContractTests
{
    [Fact]
    public void IntegrationOwnsNullableSessionPlatformOverride()
    {
        var property = typeof(Integration).GetProperty("SessionPlatformId");

        Assert.NotNull(property);
        Assert.Equal(typeof(long?), property.PropertyType);
    }

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
        Assert.Equal(["Steam", "Psn", "Xbox", "Igdb", "Gog", "Epic"], Enum.GetNames<GameExternalIdentityType>());
        Assert.Equal(["Steam", "Psn", "Xbox", "Api"], Enum.GetNames<IntegrationType>());
        Assert.Equal(["Polling", "Api", "Manual"], Enum.GetNames<CaptureMethod>());
    }

    [Fact]
    public void AdminRoutesLiveOnlyInTheAdminModule()
    {
        var program = File.ReadAllText(Path.Combine(FindApiRoot(), "Program.cs"));
        Assert.Contains("MapAdmin", program);
        Assert.Contains("AddAdmin", program);

        var endpoints = File.ReadAllText(Path.Combine(FindApiRoot(), "Modules", "Admin", "AdminEndpoints.cs"));
        Assert.Contains("RequireAuthorization(AdministratorRequirement.PolicyName)", endpoints);

        var otherModules = Directory
            .EnumerateFiles(Path.Combine(FindApiRoot(), "Modules"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.Combine("Modules", "Admin"), StringComparison.Ordinal))
            .Select(path => File.ReadAllText(path));
        Assert.DoesNotContain(otherModules, content => content.Contains("\"/admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ApiHasNoAdminProjectOrServiceReference()
    {
        var project = File.ReadAllText(Path.Combine(FindApiRoot(), "Api.csproj"));
        Assert.DoesNotContain("Admin", project);
    }

    private static string FindApiRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Api");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Could not locate src/Api.");
    }
}
