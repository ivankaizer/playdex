namespace Playdex.Core.Tests;

public sealed class AppShortcutsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("playdex-apps-").FullName;

    private static readonly ResolvedApp _edge =
        new("browser", "Microsoft Edge", "browser", @"C:\Edge\msedge.exe", [], @"C:\Edge", "msedge");

    [Fact]
    public void FindsDiscordBuildsByTheirUpdaterAndTheBrowser()
    {
        Updater("Discord");
        Updater("DiscordCanary");

        var apps = AppShortcuts.Detected(_directory, () => _edge);

        Assert.Equal(["discord", "discord-canary", "browser"], apps.Select(a => a.Id));
        Assert.Equal(["Discord", "Discord Canary", "Microsoft Edge"], apps.Select(a => a.Name));
        Assert.Empty(AppShortcuts.Detected(Path.Combine(_directory, "nothing"), () => null));
    }

    [Fact]
    public void DiscordStartsThroughItsUpdaterAndRunsFromItsFolder()
    {
        var updater = Updater("Discord");
        var app = new AppShortcut { Id = "discord", Name = "Discord", Kind = "discord", Arguments = ["--start-minimized"] };

        var resolved = AppShortcuts.Resolve(app, _directory, () => null)!;

        Assert.Equal(updater, resolved.ExecutablePath);
        Assert.Equal(["--processStart", "Discord.exe", "--start-minimized"], resolved.Arguments);
        Assert.Equal(Path.Combine(_directory, "Discord"), resolved.ProcessDirectory);
        Assert.Equal("Discord", resolved.ProcessName);
        Assert.Null(AppShortcuts.Resolve(app, Path.Combine(_directory, "elsewhere"), () => null));
    }

    [Fact]
    public void TheBrowserIsWhicheverIsDefaultNowUnderItsSavedName()
    {
        var app = new AppShortcut { Id = "web", Name = "Web", Kind = "browser" };

        var resolved = AppShortcuts.Resolve(app, _directory, () => _edge)!;

        Assert.Equal(("web", "Web", @"C:\Edge\msedge.exe"), (resolved.Id, resolved.Name, resolved.ExecutablePath));
        Assert.Null(AppShortcuts.Resolve(app, _directory, () => null));
    }

    [Fact]
    public void AnExecutableIsOnlyThereWhileItsFileIs()
    {
        var exe = File(Path.Combine(_directory, "Spotify", "Spotify.exe"));
        var app = new AppShortcut { Id = "spotify", Name = "Spotify", ExecutablePath = exe };

        Assert.Equal("Spotify", AppShortcuts.Resolve(app, _directory, () => null)!.ProcessName);
        System.IO.File.Delete(exe);
        Assert.Null(AppShortcuts.Resolve(app, _directory, () => null));
    }

    [Fact]
    public void FindsTheAppsProcessesByNameUnderItsFolder()
    {
        var self = Environment.ProcessPath!;
        var app = new ResolvedApp("self", "Self", "executable", self, [], Path.GetDirectoryName(self)!, Path.GetFileNameWithoutExtension(self));

        Assert.Contains(Environment.ProcessId, AppShortcuts.ProcessIds(app));
        Assert.DoesNotContain(Environment.ProcessId, AppShortcuts.ProcessIds(app with { ProcessDirectory = _directory }));
    }

    [Fact]
    public void AddingAnExecutableGivesItAUniqueIdAndRefusesTheSameFileTwice()
    {
        var config = new AppConfig { Apps = [new AppShortcut { Id = "tool", Name = "Other", ExecutablePath = @"C:\Other\tool.exe" }] };
        var exe = File(Path.Combine(_directory, "Tool", "Tool.exe"));

        var added = AppShortcuts.AddExecutable(config, exe);

        Assert.Equal("tool-2", added.Id);
        Assert.Equal("Tool", added.Name);
        Assert.Equal(2, config.Apps.Count);
        Assert.Throws<InvalidOperationException>(() => AppShortcuts.AddExecutable(config, exe));
        Assert.Throws<InvalidOperationException>(() => AppShortcuts.AddExecutable(config, Path.Combine(_directory, "missing.exe")));
    }

    [Fact]
    public void ChangingAndRemovingApps()
    {
        var config = new AppConfig { Apps = [new AppShortcut { Id = "discord", Name = "Discord", Kind = "discord" }] };

        AppShortcuts.Change(config, "DISCORD", " Chat ", "--start-minimized \"two words\"");
        Assert.Equal("Chat", config.Apps[0].Name);
        Assert.Equal(["--start-minimized", "two words"], config.Apps[0].Arguments);
        Assert.Throws<InvalidOperationException>(() => AppShortcuts.Change(config, "discord", "  ", null));

        AppShortcuts.Remove(config, "discord");
        Assert.Empty(config.Apps);
        Assert.Throws<InvalidOperationException>(() => AppShortcuts.Remove(config, "discord"));
    }

    [Fact]
    public void ASavedListIsTidiedAndKeptAcrossLoads()
    {
        var path = Path.Combine(_directory, "config.json");
        System.IO.File.WriteAllText(path, """
            {
              "apps": [
                { "id": "x", "name": " Spotify ", "executablePath": "C:\\Spotify\\Spotify.exe" },
                { "id": "x", "name": "", "kind": "Browser" },
                { "id": "broken", "name": "No path" },
                null
              ]
            }
            """);

        var apps = new ConfigStore(path).LoadOrCreate().Apps!;

        Assert.Equal(["x", "x-2"], apps.Select(a => a.Id));
        Assert.Equal(["Spotify", "browser"], apps.Select(a => a.Name));
        Assert.Equal(["executable", "browser"], apps.Select(a => a.Kind));
        Assert.Null(new ConfigStore(Path.Combine(_directory, "new.json")).LoadOrCreate().Apps);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Edge\\msedge.exe\" --single-argument %1", @"C:\Program Files\Edge\msedge.exe")]
    [InlineData("C:\\Firefox\\firefox.exe -osint -url \"%1\"", @"C:\Firefox\firefox.exe")]
    [InlineData("", null)]
    [InlineData("rundll32 url.dll", null)]
    public void ReadsTheExecutableOfAnOpenCommand(string command, string? expected) =>
        Assert.Equal(expected, AppShortcuts.ExecutableOfCommand(command));

    private string Updater(string folder) => File(Path.Combine(_directory, folder, "Update.exe"));

    private static string File(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, []);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
