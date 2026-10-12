namespace Playdex.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("playdex-config-").FullName;

    private string ConfigPath => Path.Combine(_directory, "Playdex", "config.json");
    private string WrapperConfig => Path.Combine(_directory, "wrapper.json");
    private string LauncherConfig => Path.Combine(_directory, "launcher.json");

    [Fact]
    public void MigratesTheWrappersGamesAndKeepsItsFolders()
    {
        File.WriteAllText(WrapperConfig, """
            {
              "api": { "baseUrl": "https://example.test/api", "apiKey": "install-key", "platform": "pc" },
              "games": {
                "hades": {
                  "gameId": "hades", "displayName": "Hades", "apiGameId": "1145360", "gameProvider": "steam",
                  "launch": { "executablePath": "C:\\Games\\Hades\\Hades.exe", "arguments": ["-dx12"] },
                  "monitor": { "mode": "executableName", "executableName": "Hades.exe", "exitDebounceSeconds": 5 }
                }
              },
              "storage": { "launcherDirectory": "D:\\Playdex\\launchers" }
            }
            """);

        var config = Store().LoadOrCreate();

        var game = config.Games["HADES"];
        Assert.Equal("Hades", game.DisplayName);
        Assert.Equal(["-dx12"], game.Launch.Arguments);
        Assert.Equal(MonitorMode.ExecutableName, game.Monitor.Mode);
        Assert.Equal("install-key", config.Api.ApiKey);
        Assert.Equal("https://example.test/api", config.Api.BaseUrl);
        Assert.Equal(@"D:\Playdex\launchers", config.Storage.LauncherDirectory);
        // Folders the old config left out were the Wrapper's defaults, not the new install's.
        Assert.Equal(Path.Combine(Paths.LegacyWrapperDirectory, "state"), config.Storage.StateDirectory);
        Assert.Equal(Path.Combine(Paths.LegacyWrapperDirectory, "logs"), config.Storage.LogDirectory);
        Assert.True(File.Exists(ConfigPath));
        Assert.True(File.Exists(WrapperConfig), "the old config is left in place");
        Assert.Contains("\"mode\": \"executableName\"", File.ReadAllText(ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void APerGameKeyBecomesTheInstallsKeyOnlyWhenItHasNone()
    {
        File.WriteAllText(WrapperConfig, """
            { "api": { "apiKey": "" }, "games": { "a": { "gameId": "a", "apiKey": " game-key " }, "b": { "gameId": "b" } } }
            """);
        Assert.Equal("game-key", Store().LoadOrCreate().Api.ApiKey);

        File.Delete(ConfigPath);
        File.WriteAllText(WrapperConfig, """
            { "api": { "apiKey": "install-key" }, "games": { "a": { "gameId": "a", "apiKey": "game-key" } } }
            """);
        var config = Store().LoadOrCreate();
        Assert.Equal("install-key", config.Api.ApiKey);
        Assert.DoesNotContain("game-key", File.ReadAllText(ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacesTheWrappersBrokenDefaultUrl()
    {
        File.WriteAllText(WrapperConfig, """{ "api": { "baseUrl": "https://playdex.ejwan.dev/api/" } }""");

        Assert.Equal(ApiConfig.DefaultBaseUrl, Store().LoadOrCreate().Api.BaseUrl);
    }

    [Fact]
    public void TakesTheLaunchersKeyAndUrlWhenThereIsNoWrapper()
    {
        File.WriteAllText(LauncherConfig, """{ "apiKey": "launcher-key", "apiBaseUrl": "https://preview.test/api" }""");

        var config = Store().LoadOrCreate();

        Assert.Equal("launcher-key", config.Api.ApiKey);
        Assert.Equal("https://preview.test/api", config.Api.BaseUrl);
        Assert.Equal(Paths.DefaultLauncherDirectory, config.Storage.LauncherDirectory);
    }

    [Fact]
    public void TheWrappersApiSettingsWinOverTheLaunchers()
    {
        File.WriteAllText(WrapperConfig, """{ "api": { "baseUrl": "https://example.test/api", "apiKey": "wrapper-key" } }""");
        File.WriteAllText(LauncherConfig, """{ "apiKey": "launcher-key", "apiBaseUrl": "https://preview.test/api" }""");

        var config = Store().LoadOrCreate();

        Assert.Equal("wrapper-key", config.Api.ApiKey);
        Assert.Equal("https://example.test/api", config.Api.BaseUrl);
    }

    [Fact]
    public void AnUnreadableWrapperConfigStillMigratesTheLauncher()
    {
        File.WriteAllText(WrapperConfig, "{ not json");
        File.WriteAllText(LauncherConfig, """{ "apiKey": "launcher-key" }""");

        Assert.Equal("launcher-key", Store().LoadOrCreate().Api.ApiKey);
    }

    [Fact]
    public async Task MigratesOnlyOnce()
    {
        File.WriteAllText(LauncherConfig, """{ "apiKey": "launcher-key" }""");
        var store = Store();
        var config = await store.LoadOrCreateAsync();
        config.Api.ApiKey = "changed";
        await store.SaveAsync(config);

        Assert.Equal("changed", (await Store().LoadOrCreateAsync()).Api.ApiKey);
    }

    [Fact]
    public void AStoreWithoutLegacyConfigsStartsEmpty()
    {
        File.WriteAllText(WrapperConfig, """{ "api": { "apiKey": "wrapper-key" } }""");

        var config = new ConfigStore(ConfigPath).LoadOrCreate();

        Assert.Equal("", config.Api.ApiKey);
        Assert.Empty(config.Games);
    }

    [Fact]
    public async Task TheModeIsDesktopUnlessBigPictureWasSaved()
    {
        var store = new ConfigStore(ConfigPath);
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "api": { "apiKey": "key" } }""");
        Assert.Equal(ShellConfig.DesktopMode, store.LoadOrCreate().Shell.Mode);

        File.WriteAllText(ConfigPath, """{ "shell": { "mode": "BIGPICTURE", "executablePath": "C:\\Playdex\\Playdex.exe" } }""");
        var config = await store.LoadOrCreateAsync();
        Assert.Equal(ShellConfig.BigPictureMode, config.Shell.Mode);
        Assert.Equal(@"C:\Playdex\Playdex.exe", config.Shell.ExecutablePath);

        config.Shell.Mode = "something else";
        await store.SaveAsync(config);
        Assert.Equal(ShellConfig.DesktopMode, store.LoadOrCreate().Shell.Mode);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ConfigStore Store() => new(ConfigPath, WrapperConfig, LauncherConfig);
}
