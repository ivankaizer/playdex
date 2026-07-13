using Xunit;

namespace Playdex.Web.Tests;

public sealed class ProjectContractTests
{
    private static readonly string WebRoot = FindWebRoot();

    [Fact]
    public void Index_HostsBlazorWithTheExistingThemeContract()
    {
        var index = ReadRequiredFile("wwwroot/index.html");
        var headStart = index.IndexOf("<head>", StringComparison.Ordinal);
        var headEnd = index.IndexOf("</head>", StringComparison.Ordinal);
        var themeBootstrap = index.IndexOf("playdex.theme", StringComparison.Ordinal);

        Assert.Contains("<base href=\"/\">", index);
        Assert.Contains("css/app.css", index);
        Assert.Contains("<body class=\"bg-slate-950 text-slate-100\">", index);
        Assert.True(headStart >= 0 && themeBootstrap > headStart && themeBootstrap < headEnd);
        Assert.Contains("prefers-color-scheme", index);
        Assert.Contains("_framework/blazor.webassembly.js", index);
    }

    [Fact]
    public void App_UsesTheNativeBlazorRouter()
    {
        Assert.Contains("<Router", ReadRequiredFile("App.razor"));
    }

    [Fact]
    public void Project_IsBlazorOnlyAndExcludesItsNestedTests()
    {
        var project = ReadRequiredFile("Playdex.Web.csproj");

        Assert.DoesNotContain("react", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DefaultItemExcludes", project);
        Assert.Contains("Playdex.Web.Tests/**", project);
    }

    private static string ReadRequiredFile(string relativePath)
    {
        var path = Path.Combine(WebRoot, relativePath);
        Assert.True(File.Exists(path), $"Missing required file: {relativePath}");
        return File.ReadAllText(path);
    }

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "apps", "web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate apps/web from the test output directory.");
    }
}
