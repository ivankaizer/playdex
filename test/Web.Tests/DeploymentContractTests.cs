using Xunit;

namespace Web.Tests;

public sealed class DeploymentContractTests
{
    private static readonly string WebRoot = FindWebRoot();
    private static readonly string RepositoryRoot = Directory.GetParent(Directory.GetParent(WebRoot)!.FullName)!.FullName;

    [Fact]
    public void ProductionImagePublishesBlazorAndServesItOnTheNginxContractPort()
    {
        var dockerfile = ReadWebFile("Dockerfile");
        var nginx = ReadWebFile("nginx.conf");
        var entrypoint = ReadWebFile("docker-entrypoint.sh");

        Assert.Contains("oven/bun:1 AS css", dockerfile);
        Assert.Contains("bun run css:build", dockerfile);
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0 AS build", dockerfile);
        Assert.Contains("dotnet publish", dockerfile);
        Assert.Contains("/app/publish/wwwroot", dockerfile);
        Assert.Contains("nginx:1.27-alpine", dockerfile);
        Assert.Contains("EXPOSE 5056", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        Assert.Contains("127.0.0.1:5056", dockerfile);
        Assert.DoesNotContain("vite build", dockerfile, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("__PLAYDEX_API_URL__", entrypoint);
        Assert.Contains("PLAYDEX_API_URL", entrypoint);

        Assert.Contains("listen 5056;", nginx);
        Assert.Contains("try_files $uri /index.html;", nginx);
    }

    [Fact]
    public void DevelopmentImageRunsTailwindAndDotnet()
    {
        var dockerfile = ReadWebFile("Dockerfile.dev");

        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0", dockerfile);
        Assert.Contains("COPY --from=bun", dockerfile);
        Assert.Contains("bun run css:watch", dockerfile);
        Assert.Contains("dotnet run", dockerfile);
        Assert.Contains("http://0.0.0.0:5173", dockerfile);
        Assert.DoesNotContain("vite", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeAndCiTargetDotnetAndBlazorArtifacts()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "docker-compose.yml"));
        var apiWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "build-api.yaml"));
        var uiWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "build-ui.yaml"));
        var testWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "test.yaml"));

        Assert.Contains("dockerfile: src/Api/Dockerfile", compose);
        Assert.Contains("dockerfile: src/Web/Dockerfile.dev", compose);
        Assert.DoesNotContain("VITE_API_URL", compose);
        Assert.Contains("dotnet_nuget", compose);
        Assert.Contains("web_bun_cache", compose);
        Assert.Contains("PLAYDEX_API_URL", compose);

        Assert.Contains("src/Api", apiWorkflow);
        Assert.Contains("src/Web", uiWorkflow);
        Assert.DoesNotContain("VITE_API_URL", apiWorkflow);
        Assert.DoesNotContain("VITE_API_URL", uiWorkflow);

        Assert.Contains("actions/setup-dotnet", testWorkflow);
        Assert.Contains("dotnet-version: '10.0.x'", testWorkflow);
        Assert.Contains("bun run css:build", testWorkflow);
        Assert.Contains("dotnet build Playdex.sln", testWorkflow);
        Assert.Contains("dotnet test test/Api.Tests/Api.Tests.csproj", testWorkflow);
        Assert.Contains("dotnet test test/Web.Tests/Web.Tests.csproj", testWorkflow);
    }

    private static string ReadWebFile(string relativePath) =>
        File.ReadAllText(Path.Combine(WebRoot, relativePath));

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate src/Web from the test output directory.");
    }
}
