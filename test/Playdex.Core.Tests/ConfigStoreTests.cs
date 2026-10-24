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

    [Theory]
    [InlineData("https://playdex-api.ejwan.dev/api", "https://dash.playdex.dev/api")]
    [InlineData("https://PLAYDEX-API.ejwan.dev/api/", "https://dash.playdex.dev/api")]
    [InlineData("https://preview.test/api", "https://preview.test/api")]
    public void ASavedRetiredDefaultMovesToTheCurrentOne(string saved, string expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, $$"""{ "api": { "baseUrl": "{{saved}}", "apiKey": "key" } }""");

        Assert.Equal(expected, new ConfigStore(ConfigPath).LoadOrCreate().Api.BaseUrl);
        Assert.Equal("https://dash.playdex.dev/api", new ConfigStore(Path.Combine(_directory, "new.json")).LoadOrCreate().Api.BaseUrl);
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

    [Fact]
    public void ACorruptConfigIsMovedAsideAndTheAppStartsWithDefaults()
    {
        // The Wrapper's old config is still there; recovering from a corrupt config doesn't migrate it again.
        File.WriteAllText(WrapperConfig, """{ "api": { "apiKey": "wrapper-key" } }""");
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "api": { "apiKey": "key" """);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => Store().LoadOrCreate());

        var config = Store().LoadOrRecover();

        Assert.Equal("", config.Api.ApiKey);
        Assert.Empty(config.Games);
        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!, "config.json.corrupt-*"));
        Assert.Equal("""{ "api": { "apiKey": "key" """, File.ReadAllText(aside));
        Assert.Equal("", Store().LoadOrCreate().Api.ApiKey); // a valid default config took its place
    }

    [Fact]
    public void AReadableConfigIsLoadedAsItIs()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "api": { "apiKey": "key" } }""");
        var store = Store();

        Assert.Equal("key", store.LoadOrRecover().Api.ApiKey);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!, "*.corrupt-*"));
        Assert.Null(store.Reset);
    }

    [Fact]
    public void AResetIsToldOnceWithWhereTheOldFileIs()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "games": """);
        var store = Store();

        store.LoadOrRecover();

        var reset = Assert.IsType<ConfigReset>(store.Reset);
        var aside = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(ConfigPath)!, "config.json.corrupt-*"));
        Assert.Equal(aside, reset.MovedTo);
        Assert.Contains(Path.GetFileName(aside), reset.Message, StringComparison.Ordinal);
        // A Windows notification's limits.
        Assert.InRange(ConfigReset.Title.Length, 1, 64);
        Assert.InRange(reset.Message.Length, 1, 256);

        // The next start reads the default config that took its place: nothing to tell.
        var next = Store();
        next.LoadOrRecover();
        Assert.Null(next.Reset);
    }

    [Fact]
    public void AResetWhoseFileCouldNotBeMovedStillFitsANotification()
    {
        var reset = new ConfigReset(ConfigPath, null);

        Assert.InRange(reset.Message.Length, 1, 256);
        Assert.DoesNotContain("corrupt", reset.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeysAreSavedEncryptedForThisWindowsUser()
    {
        var store = Store();
        var config = store.LoadOrCreate();
        config.Api.ApiKey = "plx_install-key";
        config.SteamGridDb.ApiKey = "artwork-key";
        await store.SaveAsync(config);

        var saved = File.ReadAllText(ConfigPath);
        Assert.DoesNotContain("plx_install-key", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("artwork-key", saved, StringComparison.Ordinal);
        Assert.Contains("\"apiKey\": \"dpapi:", saved, StringComparison.Ordinal);

        var loaded = Store().LoadOrCreate();
        Assert.Equal("plx_install-key", loaded.Api.ApiKey);
        Assert.Equal("artwork-key", loaded.SteamGridDb.ApiKey);
        Assert.Equal(saved, File.ReadAllText(ConfigPath)); // nothing to encrypt: not saved again
    }

    [Fact]
    public async Task KeysSavedInPlainTextAreEncryptedWhenLoaded()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        const string PlainText = """{ "api": { "apiKey": "plx_old" }, "steamGridDb": { "apiKey": "artwork-old" } }""";
        File.WriteAllText(ConfigPath, PlainText);

        var config = Store().LoadOrCreate();

        Assert.Equal("plx_old", config.Api.ApiKey);
        Assert.Equal("artwork-old", config.SteamGridDb.ApiKey);
        var saved = File.ReadAllText(ConfigPath);
        Assert.DoesNotContain("plx_old", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("artwork-old", saved, StringComparison.Ordinal);

        File.WriteAllText(ConfigPath, PlainText);
        Assert.Equal("plx_old", (await Store().LoadOrCreateAsync()).Api.ApiKey);
        Assert.DoesNotContain("plx_old", File.ReadAllText(ConfigPath), StringComparison.Ordinal);
    }

    [Fact]
    public void AReadOnlyConfigWithAPlainTextKeyStillLoads()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "api": { "apiKey": "plx_old" } }""");
        // Saving replaces the file, which Windows refuses for a read-only one.
        var folder = new DirectoryInfo(Path.GetDirectoryName(ConfigPath)!);
        File.SetAttributes(ConfigPath, FileAttributes.ReadOnly);
        try
        {
            Assert.Equal("plx_old", Store().LoadOrCreate().Api.ApiKey);
            Assert.Contains("plx_old", File.ReadAllText(ConfigPath), StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(ConfigPath, FileAttributes.Normal);
        }
        Assert.Empty(folder.GetFiles("*.tmp"));
    }

    [Fact]
    public void AKeyThisUserCantDecryptReadsAsNone()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        // As if copied from another PC: a DPAPI value this user's keys don't open, and one that isn't base64.
        File.WriteAllText(ConfigPath, """
            { "api": { "apiKey": "dpapi:AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA" }, "steamGridDb": { "apiKey": "dpapi:not base64" } }
            """);

        var store = Store();
        var config = store.LoadOrRecover();

        Assert.Equal("", config.Api.ApiKey);
        Assert.Equal("", config.SteamGridDb.ApiKey);
        Assert.Null(store.Reset); // the rest of the config is kept
    }

    [Fact]
    public void AConfigSavedWithABomIsRead()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, """{ "api": { "baseUrl": "https://example.test/api" } }""", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal("https://example.test/api", Store().LoadOrCreate().Api.BaseUrl);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ConfigStore Store() => new(ConfigPath, WrapperConfig, LauncherConfig);
}
