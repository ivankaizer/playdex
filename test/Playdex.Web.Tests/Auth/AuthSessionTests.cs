using Playdex.Web.Shared.Auth;
using Playdex.Web.Shared.Browser;
using Xunit;

namespace Playdex.Web.Tests.Auth;

public sealed class AuthSessionTests
{
    [Fact]
    public async Task StoreLogin_WritesExactlyTheFourReactAuthKeys()
    {
        var storage = new MemoryBrowserStorage();
        var session = new AuthSession(storage);

        await session.StoreLoginAsync(new LoginResponse(42, "player@example.com", true, "access", "refresh"));

        Assert.Equal(
            [
                "set:playdex.token:access",
                "set:playdex.refresh:refresh",
                "set:playdex.email:player@example.com",
                "set:playdex.isAdmin:1",
            ],
            storage.Operations);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", true)]
    [InlineData("token", true)]
    public async Task IsLoggedIn_UsesNonEmptyAccessToken(string? token, bool expected)
    {
        var session = new AuthSession(new MemoryBrowserStorage(("playdex.token", token)));

        Assert.Equal(expected, await session.IsLoggedInAsync());
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", false)]
    [InlineData("0", false)]
    [InlineData(" 1", false)]
    [InlineData(null, false)]
    public async Task IsAdmin_AcceptsOnlyExactOne(string? value, bool expected)
    {
        var session = new AuthSession(new MemoryBrowserStorage(("playdex.isAdmin", value)));

        Assert.Equal(expected, await session.IsAdminAsync());
    }

    [Fact]
    public async Task ProfileAndTokenUpdates_WriteOnlyTheirOwnKeys()
    {
        var storage = new MemoryBrowserStorage();
        var session = new AuthSession(storage);

        await session.StoreProfileAsync(new MeResponse(42, "new@example.com", false));
        await session.StoreTokensAsync("new-access", "new-refresh");

        Assert.Equal(
            [
                "set:playdex.email:new@example.com",
                "set:playdex.isAdmin:0",
                "set:playdex.token:new-access",
                "set:playdex.refresh:new-refresh",
            ],
            storage.Operations);
    }

    [Fact]
    public async Task Logout_RemovesOnlyAuthKeysAndPreservesTheme()
    {
        var storage = new MemoryBrowserStorage(
            ("playdex.token", "access"),
            ("playdex.refresh", "refresh"),
            ("playdex.email", "player@example.com"),
            ("playdex.isAdmin", "1"),
            ("playdex.theme", "dark"));
        var session = new AuthSession(storage);

        await session.LogoutAsync();

        Assert.Equal(
            [
                "remove:playdex.token",
                "remove:playdex.refresh",
                "remove:playdex.email",
                "remove:playdex.isAdmin",
            ],
            storage.Operations);
        Assert.Equal("dark", await storage.GetAsync("playdex.theme"));
    }

    [Fact]
    public async Task ClearAdmin_RemovesOnlyTheCachedAdminFlag()
    {
        var storage = new MemoryBrowserStorage(
            ("playdex.token", "access"),
            ("playdex.email", "player@example.com"),
            ("playdex.isAdmin", "1"));
        var session = new AuthSession(storage);

        await session.ClearAdminAsync();

        Assert.Equal(["remove:playdex.isAdmin"], storage.Operations);
        Assert.Equal("access", await storage.GetAsync("playdex.token"));
        Assert.Equal("player@example.com", await storage.GetAsync("playdex.email"));
    }

    [Fact]
    public async Task StoreAdmin_UpdatesOnlyTheCachedAdminFlag()
    {
        var storage = new MemoryBrowserStorage(("playdex.email", "player@example.com"));
        var session = new AuthSession(storage);

        await session.StoreAdminAsync(true);

        Assert.Equal(["set:playdex.isAdmin:1"], storage.Operations);
        Assert.Equal("player@example.com", await storage.GetAsync("playdex.email"));
    }

    [Fact]
    public void BrowserRuntime_ExposesOnlyTheRequiredGlobalInteropAndLoadsBeforeBlazor()
    {
        var webRoot = FindWebRoot();
        var scriptPath = Path.Combine(webRoot, "wwwroot", "js", "playdex.js");
        Assert.True(File.Exists(scriptPath), "Missing browser interop script.");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("playdex.storage", script);
        Assert.Contains("applyTheme", script);
        Assert.Contains("prefersDark", script);
        Assert.Contains("window.confirm", script);
        Assert.Contains("getElementById", script);
        Assert.Contains("behavior: \"smooth\"", script);

        var index = File.ReadAllText(Path.Combine(webRoot, "wwwroot", "index.html"));
        var interop = index.IndexOf("js/playdex.js", StringComparison.Ordinal);
        var blazor = index.IndexOf("_framework/blazor.webassembly.js", StringComparison.Ordinal);
        Assert.True(interop >= 0 && blazor > interop, "Browser interop must load immediately before Blazor.");
    }

    [Fact]
    public void Program_RegistersAuthBrowserInteropAndApiServicesAsScoped()
    {
        var program = File.ReadAllText(Path.Combine(FindWebRoot(), "Program.cs"));

        Assert.Contains("AddScoped<IBrowserStorage, BrowserStorage>()", program);
        Assert.Contains("AddScoped<AuthSession>()", program);
        Assert.Contains("AddScoped<BrowserInterop>()", program);
        Assert.Contains("AddScoped<PlaydexApiClient>", program);
        Assert.Contains("Configuration[\"ApiBaseUrl\"]", program);
        Assert.Contains("__PLAYDEX_API_URL__", program);
        Assert.Contains("/api/", program);
    }

    private static string FindWebRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Playdex.Web");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate src/Playdex.Web from the test output directory.");
    }

    private sealed class MemoryBrowserStorage(params (string Key, string? Value)[] values) : IBrowserStorage
    {
        private readonly Dictionary<string, string?> _values = values.ToDictionary(item => item.Key, item => item.Value);

        public List<string> Operations { get; } = [];

        public ValueTask<string?> GetAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return ValueTask.FromResult(value);
        }

        public ValueTask SetAsync(string key, string value)
        {
            _values[key] = value;
            Operations.Add($"set:{key}:{value}");
            return ValueTask.CompletedTask;
        }

        public ValueTask RemoveAsync(string key)
        {
            _values.Remove(key);
            Operations.Add($"remove:{key}");
            return ValueTask.CompletedTask;
        }
    }
}
