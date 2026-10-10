using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class SteamShortcutsTests
{
    [Fact]
    public Task TestAddSteamShortcutAndAvoidDuplicate()
    {
        var directory = NewTempDir();
        var userConfig = Path.Combine(directory, "userdata", "123", "config");
        var launcher = Path.Combine(directory, "launchers", "Game.exe");
        var executable = Path.Combine(directory, "games", "Original.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(launcher)!);
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllBytes(launcher, [0x4d, 0x5a]);
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        var game = new GameConfig
        {
            GameId = "game",
            DisplayName = "Game & Friends",
            Launch = new LaunchConfig { ExecutablePath = executable }
        };

        var added = SteamIntegration.AddShortcut(game, launcher, userConfig);
        var shortcutsPath = Path.Combine(userConfig, "shortcuts.vdf");
        var firstBytes = File.ReadAllBytes(shortcutsPath);
        var duplicate = SteamIntegration.AddShortcut(game, launcher, userConfig);

        Assert.True(added.WasAdded);
        Assert.True((added.AppId & 0x80000000u) != 0);
        Assert.True(!duplicate.WasAdded);
        Assert.Equal(added.AppId, duplicate.AppId);
        Assert.Equal(firstBytes, File.ReadAllBytes(shortcutsPath));
        Assert.Contains("Game & Friends", Encoding.UTF8.GetString(firstBytes));
        Assert.Contains($"\"{Path.GetFullPath(launcher)}\"", Encoding.UTF8.GetString(firstBytes));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestAddingAnotherShortcutPreservesExistingFileAndCreatesBackup()
    {
        var directory = NewTempDir();
        var userConfig = Path.Combine(directory, "config");
        var firstLauncher = CreateExecutable(directory, "First.exe");
        var secondLauncher = CreateExecutable(directory, "Second.exe");
        var first = new GameConfig { GameId = "first", DisplayName = "First", Launch = new LaunchConfig { ExecutablePath = firstLauncher } };
        var second = new GameConfig { GameId = "second", DisplayName = "Second", Launch = new LaunchConfig { ExecutablePath = secondLauncher } };

        var firstResult = SteamIntegration.AddShortcut(first, firstLauncher, userConfig);
        var secondResult = SteamIntegration.AddShortcut(second, secondLauncher, userConfig);
        var firstAgain = SteamIntegration.AddShortcut(first, firstLauncher, userConfig);

        Assert.True(firstResult.WasAdded);
        Assert.True(secondResult.WasAdded);
        Assert.True(!firstAgain.WasAdded);
        Assert.Equal(firstResult.AppId, firstAgain.AppId);
        Assert.True(File.Exists(Path.Combine(userConfig, "shortcuts.vdf.playdex.bak")));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TestSteamGridDbDownloadsEveryArtworkType()
    {
        var directory = NewTempDir();
        var handler = new SteamGridDbHandler();
        using var http = new HttpClient(handler);
        var client = new SteamGridDbClient(http);
        var game = new GameConfig
        {
            DisplayName = "Portal 2",
            ApiGameId = "620"
        };

        var result = await client.SynchronizeAsync("secret-key", game, directory, 2147485000u);

        Assert.Equal(5, result.DownloadedCount);
        Assert.Equal(5, result.AvailableCount);
        var grid = Path.Combine(directory, "grid");
        Assert.True(File.Exists(Path.Combine(grid, "2147485000p.png")));
        Assert.True(File.Exists(Path.Combine(grid, "2147485000.png")));
        Assert.True(File.Exists(Path.Combine(grid, "2147485000_hero.jpg")));
        Assert.True(File.Exists(Path.Combine(grid, "2147485000_logo.png")));
        Assert.True(File.Exists(Path.Combine(grid, "2147485000_icon.ico")));
        Assert.Contains(handler.Requests, request => request.Uri.EndsWith("/games/steam/620", StringComparison.Ordinal));
        Assert.All(
            handler.Requests.Where(request => request.Uri.Contains("/api/v2/", StringComparison.Ordinal)),
            request => Assert.Equal("Bearer secret-key", request.Authorization));
    }

    private static string CreateExecutable(string directory, string name)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, [0x4d, 0x5a]);
        return path;
    }
}

internal sealed record SteamGridDbRequest(string Uri, string Authorization);

internal sealed class SteamGridDbHandler : HttpMessageHandler
{
    public ConcurrentBag<SteamGridDbRequest> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? "";
        Requests.Add(new SteamGridDbRequest(uri, request.Headers.Authorization?.ToString() ?? ""));
        if (!uri.Contains("/api/v2/", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            });
        }

        var json = uri.Contains("/games/steam/", StringComparison.Ordinal)
            ? """{"success":true,"data":{"id":42,"name":"Portal 2"}}"""
            : AssetResponse(uri);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    private static string AssetResponse(string uri)
    {
        var image = uri.Contains("/heroes/", StringComparison.Ordinal) ? "hero.jpg"
            : uri.Contains("/logos/", StringComparison.Ordinal) ? "logo.png"
            : uri.Contains("/icons/", StringComparison.Ordinal) ? "icon.ico"
            : uri.Contains("600x900", StringComparison.Ordinal) ? "portrait.png"
            : "wide.png";
        return $$"""{"success":true,"data":[{"id":1,"score":10,"url":"https://cdn.example.test/{{image}}"}]}""";
    }
}
