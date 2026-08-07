using Xunit;

namespace Web.Tests;

public sealed class DeploymentContractTests
{
    private static readonly string WebRoot = FindWebRoot();
    private static readonly string RepositoryRoot = Directory.GetParent(Directory.GetParent(WebRoot)!.FullName)!.FullName;

    [Fact]
    public void ProductionImage_PublishesBlazorAndServesItOnTheNginxContractPort()
    {
        var dockerfile = ReadWebFile("Dockerfile");
        var nginx = ReadWebFile("nginx.conf");

        Assert.Contains("oven/bun:1 AS css", dockerfile);
        Assert.Contains("bun run css:build", dockerfile);
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0 AS build", dockerfile);
        Assert.Contains("dotnet publish", dockerfile);
        Assert.Contains("ARG PLAYDEX_API_URL", dockerfile);
        Assert.Contains("__PLAYDEX_API_URL__", dockerfile);
        Assert.Contains("/app/publish/wwwroot", dockerfile);
        Assert.Contains("nginx:1.27-alpine", dockerfile);
        Assert.Contains("EXPOSE 5056", dockerfile);
        Assert.Contains("HEALTHCHECK", dockerfile);
        Assert.Contains("127.0.0.1:5056", dockerfile);
        Assert.DoesNotContain("vite build", dockerfile, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("listen 5056;", nginx);
        Assert.Contains("try_files $uri /index.html;", nginx);
    }

    [Fact]
    public void DevelopmentImage_RunsTailwindAndDotnetWatch()
    {
        var dockerfile = ReadWebFile("Dockerfile.dev");

        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0", dockerfile);
        Assert.Contains("COPY --from=bun", dockerfile);
        Assert.Contains("bun run css:watch", dockerfile);
        Assert.Contains("dotnet watch --project Web.csproj", dockerfile);
        Assert.Contains("http://0.0.0.0:5173", dockerfile);
        Assert.DoesNotContain("vite", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComposeAndCi_TargetDotnetAndBlazorArtifacts()
    {
        var compose = File.ReadAllText(Path.Combine(RepositoryRoot, "docker-compose.yml"));
        var buildWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "build.yaml"));
        var testWorkflow = File.ReadAllText(Path.Combine(RepositoryRoot, ".github", "workflows", "test.yaml"));

        Assert.Contains("context: ./apps/api-dotnet", compose);
        Assert.DoesNotContain("VITE_API_URL", compose);
        Assert.Contains("dotnet_nuget", compose);
        Assert.Contains("web_bun_cache", compose);

        Assert.Contains("apps/api-dotnet", buildWorkflow);
        Assert.Contains("PLAYDEX_API_URL", buildWorkflow);
        Assert.DoesNotContain("apps/api\n", buildWorkflow);
        Assert.DoesNotContain("VITE_API_URL", buildWorkflow);

        Assert.Contains("actions/setup-dotnet", testWorkflow);
        Assert.Contains("dotnet-version: '10.0.x'", testWorkflow);
        Assert.Contains("bun run css:build", testWorkflow);
        Assert.Contains("dotnet test Playdex.sln", testWorkflow);
        Assert.Contains("dotnet build Playdex.sln", testWorkflow);
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

        throw new DirectoryNotFoundException("Could not locate Could not locate src/Web from the test output directory.");
    }
}
