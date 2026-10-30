namespace Playdex.Core.Tests;

public sealed class StoreGamesTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("playdex-stores-").FullName;

    [Fact]
    public void FindsAnEpicGameStartedThroughTheLauncher()
    {
        var executable = Executable("Harbor Lights", @"Binaries\Win64\HarborLights.exe");
        Manifest("harbor", appName: "Harbor", folder: Path.Combine(_directory, "Harbor Lights"), executable: @"Binaries\Win64\HarborLights.exe");

        var game = Assert.Single(EpicLibrary.Scan(Manifests));

        Assert.Equal(("epic", "Harbor", "Harbor", "Harbor Lights"), (game.Store, game.Id, game.RecordedId, game.Title));
        Assert.Equal(executable, game.ExecutablePath);
        Assert.Equal("com.epicgames.launcher://apps/ns1%3Aitem1%3AHarbor?action=launch&silent=true", game.Launch.Uri);
        Assert.False(game.StartsDirectly);
    }

    [Fact]
    public void LeavesOutEpicAddOnsPluginsAndUnfinishedInstalls()
    {
        Executable("Harbor Lights", "HarborLights.exe");
        var folder = Path.Combine(_directory, "Harbor Lights");
        Manifest("addon", appName: "HarborSoundtrack", folder: folder, executable: "HarborLights.exe", main: "Harbor");
        Manifest("plugin", appName: "Plugin", folder: folder, executable: "HarborLights.exe", categories: """["plugins", "engine"]""");
        Manifest("unfinished", appName: "Unfinished", folder: folder, executable: "HarborLights.exe", incomplete: true);
        Manifest("missing", appName: "Missing", folder: folder, executable: "Missing.exe");
        File.WriteAllText(Path.Combine(Manifests, "broken.item"), "{ not json");

        Assert.Empty(EpicLibrary.Scan(Manifests));
        Assert.Empty(EpicLibrary.Scan(Path.Combine(_directory, "no-launcher")));
    }

    [Fact]
    public void FindsAGogGameStartedDirectlyWithItsParameters()
    {
        var executable = Executable("Old Lighthouse", @"DOSBOX\dosbox.exe");
        var values = Gog("1207658930", "Old Lighthouse", executable, "-conf \"..\\lighthouse.conf\" -noconsole", Path.Combine(_directory, "Old Lighthouse"));

        var game = GogLibrary.Read(values.GetValueOrDefault)!;

        Assert.Equal(("gog", "1207658930", "1207658930", "Old Lighthouse"), (game.Store, game.Id, game.RecordedId, game.Title));
        Assert.Equal(executable, game.ExecutablePath);
        Assert.Equal(["-conf", @"..\lighthouse.conf", "-noconsole"], game.Launch.Arguments);
        Assert.Equal(Path.Combine(_directory, "Old Lighthouse"), game.Launch.WorkingDirectory);
        Assert.True(game.StartsDirectly);
    }

    [Fact]
    public void LeavesOutGogDlcsAndMissingGames()
    {
        var executable = Executable("Old Lighthouse", "Lighthouse.exe");
        var dlc = Gog("1207658931", "Old Lighthouse: Keeper", executable, null, null);
        dlc["dependsOn"] = "1207658930";
        var missing = Gog("1207658932", "Gone", Path.Combine(_directory, "Gone", "Gone.exe"), null, null);

        Assert.Null(GogLibrary.Read(dlc.GetValueOrDefault));
        Assert.Null(GogLibrary.Read(missing.GetValueOrDefault));
    }

    [Theory]
    [InlineData("xbox")]
    [InlineData("epic")]
    public void AGameItsLauncherStartsIsFollowedByItsExecutableAndRecordedByItsStoreId(string store)
    {
        var config = new AppConfig();
        var launch = store == "xbox"
            ? new LaunchConfig { ExecutablePath = @"C:\Games\Harbor\Harbor.exe", AppUserModelId = "Contoso.Harbor_8wekyb3d8bbwe!Game" }
            : new LaunchConfig { ExecutablePath = @"C:\Games\Harbor\Harbor.exe", Uri = "com.epicgames.launcher://apps/ns%3Aitem%3AHarbor?action=launch&silent=true" };
        var found = new StoreGame(store, "harbor-id", "Harbor Lights", "1234", launch);

        var game = GameCatalog.AddFromStore(config, found);

        Assert.Same(game, config.Games["Harbor"]);
        Assert.Equal("Harbor Lights", game.DisplayName);
        Assert.Equal((store, "1234"), (game.GameProvider, game.ApiGameId));
        Assert.Equal((store, "harbor-id"), (game.Store, game.StoreId));
        Assert.Same(launch, game.Launch);
        Assert.Equal((MonitorMode.ExecutablePath, @"C:\Games\Harbor\Harbor.exe"), (game.Monitor.Mode, game.Monitor.ExecutablePath));
        var again = Assert.Throws<InvalidOperationException>(() => GameCatalog.AddFromStore(config, found));
        Assert.Contains("already in Playdex", again.Message);
    }

    [Fact]
    public void AGogGameIsStartedLikeAnAddedExecutableAndTakesTheNextFreeId()
    {
        var config = new AppConfig();
        config.Games.Add("Lighthouse", new GameConfig { GameId = "Lighthouse" });
        var found = new StoreGame("gog", "1207658930", "Old Lighthouse", "1207658930", new LaunchConfig { ExecutablePath = @"C:\GOG\Lighthouse\Lighthouse.exe" });

        var game = GameCatalog.AddFromStore(config, found);

        Assert.Equal("Lighthouse-2", game.GameId);
        Assert.Equal(("gog", "1207658930"), (game.GameProvider, game.ApiGameId));
        Assert.Equal(MonitorMode.LaunchedProcess, game.Monitor.Mode);
    }

    [Fact]
    public void AGameWithoutAStoreIdIsRecordedLikeAnyOtherGame()
    {
        var found = new StoreGame("xbox", "Contoso.Tool_8wekyb3d8bbwe!App", "Tool", null,
            new LaunchConfig { ExecutablePath = @"C:\XboxGames\Tool\Content\Tool.exe", AppUserModelId = "Contoso.Tool_8wekyb3d8bbwe!App" });

        var game = GameCatalog.AddFromStore(new AppConfig(), found);

        Assert.Equal(("steam", "Tool"), (game.GameProvider, game.ApiGameId));
    }

    private string Manifests => Path.Combine(_directory, "Manifests");

    private string Executable(string folder, string relativePath)
    {
        var path = Path.Combine(_directory, folder, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    private void Manifest(
        string file, string appName, string folder, string executable, string? main = null,
        string categories = """["public", "games", "applications"]""", bool incomplete = false)
    {
        Directory.CreateDirectory(Manifests);
        File.WriteAllText(Path.Combine(Manifests, $"{file}.item"), $$"""
            {
              "FormatVersion": 0,
              "bIsIncompleteInstall": {{(incomplete ? "true" : "false")}},
              "LaunchExecutable": "{{executable.Replace(@"\", @"\\")}}",
              "DisplayName": "Harbor Lights",
              "InstallLocation": "{{folder.Replace(@"\", @"\\")}}",
              "AppCategories": {{categories}},
              "CatalogNamespace": "ns1",
              "CatalogItemId": "item1",
              "AppName": "{{appName}}",
              "MainGameAppName": "{{main ?? appName}}"
            }
            """);
    }

    private static Dictionary<string, string?> Gog(string id, string name, string executable, string? parameters, string? workingDirectory) => new()
    {
        ["gameID"] = id,
        ["gameName"] = name,
        ["exe"] = executable,
        ["launchParam"] = parameters,
        ["workingDir"] = workingDirectory,
        ["dependsOn"] = "",
    };

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
