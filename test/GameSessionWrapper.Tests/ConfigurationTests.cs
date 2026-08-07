using GameSessionWrapper;
using GameSessionWrapper.UI;
using static GameSessionWrapper.Tests.TestPaths;

namespace GameSessionWrapper.Tests;

public sealed class ConfigurationTests
{
    [Fact]
    public async Task TestConfigRoundTrip()
    {
        var path = Path.Combine(NewTempDir(), "config.json");
        var store = new ConfigStore(path);
        var config = await store.LoadOrCreateAsync();
        var game = config.GetOrCreateGame("demo");
        game.Launch.ExecutablePath = "C:\\Games\\Demo\\demo.exe";
        game.ApiGameId = "1234";
        game.GameProvider = "steam";
        game.ApiKey = "game-key";
        config.Api.ApiKey = "global-key";
        await store.SaveAsync(config);

        var loaded = await store.LoadOrCreateAsync();
        Assert.Equal("C:\\Games\\Demo\\demo.exe", loaded.Games["demo"].Launch.ExecutablePath);
        Assert.Equal("1234", loaded.Games["demo"].ApiGameId);
        Assert.Equal("steam", loaded.Games["demo"].GameProvider);
        Assert.Equal("game-key", loaded.Api.ResolveApiKey(loaded.Games["demo"]));
    }

    [Fact]
    public async Task TestStoragePathsAreNormalized()
    {
        var path = Path.Combine(NewTempDir(), "config.json");
        var store = new ConfigStore(path);
        var config = new WrapperConfig
        {
            Storage = new StorageConfig
            {
                StateDirectory = "relative-state",
                LogDirectory = "",
                LauncherDirectory = "relative-launchers"
            }
        };

        await store.SaveAsync(config);
        var loaded = await store.LoadOrCreateAsync();

        Assert.Equal(Path.GetFullPath("relative-state"), loaded.Storage.StateDirectory);
        Assert.Equal(AppBrand.DefaultLogDirectory, loaded.Storage.LogDirectory);
        Assert.Equal(Path.GetFullPath("relative-launchers"), loaded.Storage.LauncherDirectory);
        Assert.True(Path.IsPathFullyQualified(new StorageConfig().StateDirectory));
        Assert.True(Path.IsPathFullyQualified(new StorageConfig().LogDirectory));
        Assert.True(Path.IsPathFullyQualified(new StorageConfig().LauncherDirectory));
    }

    [Fact]
    public async Task TestConfigComparerRoundTrip()
    {
        var path = Path.Combine(NewTempDir(), "config.json");
        var store = new ConfigStore(path);
        var config = new WrapperConfig();
        config.GetOrCreateGame("Demo").Launch.ExecutablePath = "C:\\Games\\Demo.exe";
        await store.SaveAsync(config);

        var loaded = await store.LoadOrCreateAsync();

        Assert.Equal("Demo", loaded.GetGameForLaunch("DEMO").GameId);
        Assert.Equal(loaded.Games["Demo"], loaded.GetOrCreateGame("demo"));
        Assert.Single(loaded.Games);
    }

    [Fact]
    public async Task TestCancelledConfigSave()
    {
        var directory = NewTempDir();
        var path = Path.Combine(directory, "config.json");
        var store = new ConfigStore(path);
        var original = new WrapperConfig();
        original.GetOrCreateGame("original");
        await store.SaveAsync(original);

        var replacement = new WrapperConfig();
        replacement.GetOrCreateGame("replacement");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            await store.SaveAsync(replacement, cancellation.Token);
            throw new InvalidOperationException("Expected the save to be cancelled.");
        }
        catch (OperationCanceledException)
        {
        }

        var loaded = await store.LoadOrCreateAsync();
        Assert.True(loaded.Games.ContainsKey("original"));
        Assert.True(!loaded.Games.ContainsKey("replacement"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task TestMultiGameConfiguration()
    {
        var path = Path.Combine(NewTempDir(), "config.json");
        var store = new ConfigStore(path);
        var config = await store.LoadOrCreateAsync();
        config.GetOrCreateGame("first").Launch.ExecutablePath = "C:\\Games\\First\\first.exe";
        config.GetOrCreateGame("second").Launch.ExecutablePath = "C:\\Games\\Second\\second.exe";
        await store.SaveAsync(config);

        var loaded = await store.LoadOrCreateAsync();
        Assert.Equal("C:\\Games\\First\\first.exe", loaded.GetGameForLaunch("first").Launch.ExecutablePath);
        Assert.Equal("C:\\Games\\Second\\second.exe", loaded.GetGameForLaunch("second").Launch.ExecutablePath);
    }

    [Fact]
    public Task TestAddGameFromExecutable()
    {
        var executable = Path.Combine(NewTempDir(), "Demo Game.exe");
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        var config = new WrapperConfig();

        var game = GameCatalog.AddFromExecutable(config, executable);

        Assert.Equal("Demo Game", game.GameId);
        Assert.Equal("Demo Game", game.DisplayName);
        Assert.Equal("Demo Game", game.ApiGameId);
        Assert.Equal(Path.GetFullPath(executable), game.Launch.ExecutablePath);
        Assert.Equal(game, config.GetGameForLaunch("Demo Game"));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestSteamAppIdDiscovery()
    {
        var directory = NewTempDir();
        var executable = Path.Combine(directory, "Portal 2.exe");
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        File.WriteAllText(Path.Combine(directory, "steam_appid.txt"), $"{Environment.NewLine}  620  {Environment.NewLine}");

        var game = GameCatalog.AddFromExecutable(new WrapperConfig(), executable);

        Assert.Equal("Portal 2", game.GameId);
        Assert.Equal("620", game.ApiGameId);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestInvalidSteamAppIdFallback()
    {
        var directory = NewTempDir();
        var executable = Path.Combine(directory, "Demo.exe");
        File.WriteAllBytes(executable, [0x4d, 0x5a]);
        File.WriteAllText(Path.Combine(directory, "steam_appid.txt"), "not-an-app-id");

        var game = GameCatalog.AddFromExecutable(new WrapperConfig(), executable);

        Assert.Equal("Demo", game.ApiGameId);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TestChangeApiGameId()
    {
        var directory = NewTempDir();
        var path = Path.Combine(directory, "config.json");
        var store = new ConfigStore(path);
        var config = new WrapperConfig
        {
            Storage = new StorageConfig { LauncherDirectory = Path.Combine(directory, "launchers") }
        };
        var game = config.GetOrCreateGame("Cronos");
        game.DisplayName = "Cronos";
        game.ApiGameId = "Cronos";
        game.GameProvider = "steam";
        var launcherPath = LauncherManager.GetLauncherPath(config.Storage, game);

        var changed = GameCatalog.ChangeApiGameId(config, "Cronos", "  1850570  ");
        Assert.Equal(game, changed);
        Assert.Equal("1850570", game.ApiGameId);
        Assert.True(config.Games.ContainsKey("Cronos"));
        Assert.Equal("Cronos", game.GameId);
        Assert.Equal(launcherPath, LauncherManager.GetLauncherPath(config.Storage, game));
        Assert.Equal("Steam - 1850570", TrayGamePresentation.From(game).Subtitle);

        await store.SaveAsync(config);
        var loaded = await store.LoadOrCreateAsync();
        Assert.Equal("1850570", loaded.Games["Cronos"].ApiGameId);
        Assert.Equal("Cronos", loaded.Games["Cronos"].GameId);

        try
        {
            GameCatalog.ChangeApiGameId(config, "Cronos", "   ");
            throw new InvalidOperationException("Expected an empty game ID to be rejected.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be empty", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    [Fact]
    public async Task TestChangeDisplayName()
    {
        var directory = NewTempDir();
        var path = Path.Combine(directory, "config.json");
        var store = new ConfigStore(path);
        var config = new WrapperConfig
        {
            Storage = new StorageConfig { LauncherDirectory = Path.Combine(directory, "launchers") }
        };
        var game = config.GetOrCreateGame("Cronos");
        game.DisplayName = "Cronos";
        game.ApiGameId = "1850570";
        game.Launch.ExecutablePath = Path.Combine(directory, "Cronos.exe");
        var launcherPath = LauncherManager.GetLauncherPath(config.Storage, game);

        var changed = GameCatalog.ChangeDisplayName(config, "Cronos", "  Cronos: The New Dawn  ");
        Assert.Equal(game, changed);
        Assert.Equal("Cronos: The New Dawn", game.DisplayName);
        Assert.Equal("Cronos", game.GameId);
        Assert.Equal("1850570", game.ApiGameId);
        Assert.Equal(Path.Combine(directory, "Cronos.exe"), game.Launch.ExecutablePath);
        Assert.Equal(launcherPath, LauncherManager.GetLauncherPath(config.Storage, game));
        Assert.Equal("Cronos: The New Dawn", TrayGamePresentation.From(game).Title);

        await store.SaveAsync(config);
        var loaded = await store.LoadOrCreateAsync();
        Assert.Equal("Cronos: The New Dawn", loaded.Games["Cronos"].DisplayName);
        Assert.Equal("Cronos", loaded.Games["Cronos"].GameId);
        Assert.Equal("1850570", loaded.Games["Cronos"].ApiGameId);

        try
        {
            GameCatalog.ChangeDisplayName(config, "Cronos", "   ");
            throw new InvalidOperationException("Expected an empty game name to be rejected.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cannot be empty", StringComparison.OrdinalIgnoreCase))
        {
        }
    }

    [Fact]
    public Task TestDuplicateGameRejected()
    {
        var firstDirectory = NewTempDir();
        var secondDirectory = NewTempDir();
        var firstExecutable = Path.Combine(firstDirectory, "Demo.exe");
        var secondExecutable = Path.Combine(secondDirectory, "Demo.exe");
        File.WriteAllBytes(firstExecutable, [0x4d, 0x5a, 0x01]);
        File.WriteAllBytes(secondExecutable, [0x4d, 0x5a, 0x02]);
        var config = new WrapperConfig();
        var existing = GameCatalog.AddFromExecutable(config, firstExecutable);

        try
        {
            GameCatalog.AddFromExecutable(config, secondExecutable);
            throw new InvalidOperationException("Expected duplicate game rejection.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cannot change", StringComparison.OrdinalIgnoreCase))
        {
        }

        Assert.Single(config.Games);
        Assert.Equal(existing.Launch.ExecutablePath, config.Games["Demo"].Launch.ExecutablePath);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestRemoveGame()
    {
        var config = new WrapperConfig();
        config.GetOrCreateGame("zeta");
        config.GetOrCreateGame("Alpha");

        var removed = GameCatalog.Remove(config, "ZETA");

        Assert.Equal("zeta", removed.GameId);
        Assert.True(!config.Games.ContainsKey("zeta"));
        Assert.True(config.Games.ContainsKey("Alpha"));
        GameCatalog.Remove(config, "Alpha");
        Assert.Empty(config.Games);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestApiKeyResolution()
    {
        var api = new ApiConfig { ApiKey = "global" };
        var game = new GameConfig { ApiKey = "game" };
        Assert.Equal("game", api.ResolveApiKey(game));
        game.ApiKey = null;
        Assert.Equal("global", api.ResolveApiKey(game));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestApiSettingsNormalization()
    {
        Assert.Equal("", ApiConfig.NormalizeBaseUrl("  "));
        Assert.Equal("http://localhost:3000/api", ApiConfig.NormalizeBaseUrl("  http://localhost:3000/api/  "));
        Assert.Equal("https://example.test/api", ApiConfig.NormalizeBaseUrl("https://example.test/api"));
        Assert.True(!new ApiConfig { BaseUrl = "" }.IsEnabled);
        Assert.True(new ApiConfig { BaseUrl = "https://example.test/api" }.IsEnabled);

        foreach (var invalid in new[] { "example.test/api", "file:///C:/example", "http://" })
        {
            try
            {
                ApiConfig.NormalizeBaseUrl(invalid);
                throw new InvalidOperationException($"Expected '{invalid}' to be rejected.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API base URL", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        return Task.CompletedTask;
    }
}
