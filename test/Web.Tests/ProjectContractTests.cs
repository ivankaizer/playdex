using Xunit;

namespace Web.Tests;

public sealed class ProjectContractTests
{
    private static readonly string WebRoot = FindWebRoot();

    [Fact]
    public void IndexHostsBlazorWithTheExistingThemeContract()
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
    public void IndexBootstrapsThemeBeforeFirstPaintResources()
    {
        var index = ReadRequiredFile("wwwroot/index.html");
        var themeBootstrap = index.IndexOf("playdex.theme", StringComparison.Ordinal);
        var firstPaintStyles = index.IndexOf("html[data-theme=\"light\"]", StringComparison.Ordinal);
        var stylesheet = index.IndexOf("css/app.css", StringComparison.Ordinal);
        var blazorBoot = index.IndexOf("_framework/blazor.webassembly.js", StringComparison.Ordinal);

        Assert.True(
            themeBootstrap >= 0 &&
            firstPaintStyles > themeBootstrap &&
            stylesheet > firstPaintStyles &&
            blazorBoot > stylesheet,
            "Theme bootstrap must run before first-paint styles, the stylesheet, and Blazor startup.");
    }

    [Fact]
    public void IndexDefinesLightAndDarkLoadingColors()
    {
        var index = ReadRequiredFile("wwwroot/index.html");
        var lightStart = index.IndexOf("html[data-theme=\"light\"]", StringComparison.Ordinal);
        var darkStart = index.IndexOf("html[data-theme=\"dark\"]", StringComparison.Ordinal);
        var styleEnd = index.IndexOf("</style>", StringComparison.Ordinal);

        Assert.True(lightStart >= 0 && darkStart > lightStart && styleEnd > darkStart);

        var lightRules = index[lightStart..darkStart];
        var darkRules = index[darkStart..styleEnd];

        Assert.Contains("html[data-theme=\"light\"] body", lightRules);
        Assert.Contains("html[data-theme=\"light\"] #app", lightRules);
        Assert.Contains("background-color: #e2e8f0", lightRules);
        Assert.Contains("color: #0f172a", lightRules);
        Assert.Contains("html[data-theme=\"dark\"] body", darkRules);
        Assert.Contains("html[data-theme=\"dark\"] #app", darkRules);
        Assert.Contains("background-color: #020617", darkRules);
        Assert.Contains("color: #f1f5f9", darkRules);
    }

    [Fact]
    public void IndexAppliesFallbackThemeWhenStorageIsUnavailable()
    {
        var index = ReadRequiredFile("wwwroot/index.html");
        var storageRead = index.IndexOf("localStorage.getItem", StringComparison.Ordinal);
        Assert.True(storageRead >= 0);

        var tryStart = index.LastIndexOf("try {", storageRead, StringComparison.Ordinal);
        var catchStart = index.IndexOf("catch", storageRead, StringComparison.Ordinal);
        var themeApplication = index.IndexOf(
            "document.documentElement.classList.toggle",
            storageRead,
            StringComparison.Ordinal);

        Assert.True(
            tryStart >= 0 &&
            tryStart < storageRead &&
            catchStart > storageRead &&
            themeApplication > catchStart,
            "Storage failures must be caught before applying the fallback theme.");
        Assert.True(
            index.Contains("typeof window.matchMedia", StringComparison.Ordinal) ||
            index.Contains("window.matchMedia?.", StringComparison.Ordinal),
            "The system-theme fallback must tolerate a missing matchMedia API.");
    }

    [Fact]
    public void AppUsesTheNativeBlazorRouter()
    {
        Assert.Contains("<Router", ReadRequiredFile("App.razor"));
    }

    [Fact]
    public void ProjectIsBlazorOnlyAndTestsLiveSeparately()
    {
        var project = ReadRequiredFile("Web.csproj");

        Assert.DoesNotContain("react", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", project);
    }

    [Fact]
    public void RetiredReactRuntimeAndBuildArtifactsAreAbsent()
    {
        var retiredPaths = new[]
        {
            "src",
            "tests",
            "index.html",
            "vite.config.ts",
            "tsconfig.json",
            "eslint.config.mjs",
        };

        Assert.All(retiredPaths, relativePath =>
            Assert.False(
                File.Exists(Path.Combine(WebRoot, relativePath))
                || Directory.Exists(Path.Combine(WebRoot, relativePath)),
                $"Retired React artifact still exists: {relativePath}"));

        var package = ReadRequiredFile("package.json");
        Assert.DoesNotContain("react", package, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("vite", package, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("typescript", package, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tailwindcss", package);
        Assert.Contains("postcss", package);
        Assert.Contains("autoprefixer", package);
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
            var candidate = Path.Combine(directory.FullName, "src", "Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate src/Web from the test output directory.");
    }
}
