using System.Text.RegularExpressions;
using Xunit;

namespace Playdex.Web.Tests.Routing;

public sealed class ShellEventImportsTests
{
    [Fact]
    public void SharedEventDirectivesImportComponentsWeb()
    {
        var webRoot = FindWebRoot();
        var directives = Directory
            .EnumerateFiles(Path.Combine(webRoot, "Components"), "*.razor")
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "@on(?:change|click|input)\\b"))
            .Select(match => match.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["@onchange", "@onclick", "@oninput"], directives);
        Assert.Contains(
            "@using Microsoft.AspNetCore.Components.Web",
            File.ReadAllText(Path.Combine(webRoot, "_Imports.razor")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SharedEventDirectiveExpressionsAreRazorParseSafe()
    {
        var invalidFiles = Directory
            .EnumerateFiles(Path.Combine(FindWebRoot(), "Components"), "*.razor")
            .Where(path => File.ReadAllText(path).Contains("?? \"\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(invalidFiles);
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
