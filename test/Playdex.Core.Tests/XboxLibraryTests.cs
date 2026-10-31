namespace Playdex.Core.Tests;

public sealed class XboxLibraryTests : IDisposable
{
    private const string FullName = "Contoso.StarlightRally_1.0.12.0_x64__8wekyb3d8bbwe";

    private readonly string _directory = Directory.CreateTempSubdirectory("playdex-xbox-").FullName;

    [Fact]
    public void FindsTheGameItsAppAndItsTitleId()
    {
        var root = Package("Starlight Rally", @"Binaries\Win64\StarlightRally.exe", titleId: "6A3F8A2B");

        var game = XboxLibrary.Read(new InstalledPackage(FullName, root, null))!;

        Assert.Equal(("xbox", "Contoso.StarlightRally_8wekyb3d8bbwe!Game"), (game.Store, game.Id));
        Assert.Equal("Contoso.StarlightRally_8wekyb3d8bbwe!Game", game.Launch.AppUserModelId);
        Assert.Equal("Starlight Rally", game.Title);
        Assert.Equal(Path.Combine(root, "Binaries", "Win64", "StarlightRally.exe"), game.ExecutablePath);
        Assert.Equal("1782549035", game.RecordedId);
        Assert.Equal(root, game.InstallFolder);
        Assert.False(game.StartsDirectly);
    }

    [Fact]
    public void AResourceTitleFallsBackToTheRegisteredNameThenThePackageName()
    {
        var root = Package("ms-resource:GameDisplayName", "StarlightRally.exe");

        Assert.Equal("Starlight Rally", XboxLibrary.Read(new InstalledPackage(FullName, root, "Starlight Rally"))!.Title);
        Assert.Equal("Contoso.StarlightRally", XboxLibrary.Read(new InstalledPackage(FullName, root, null))!.Title);
    }

    [Fact]
    public void SkipsPackagesThatArentInstalledXboxGames()
    {
        var notAGame = Path.Combine(_directory, "app");
        Directory.CreateDirectory(notAGame);
        File.WriteAllText(Path.Combine(notAGame, "AppxManifest.xml"), Manifest("App"));
        var downloading = Package("Starlight Rally", "StarlightRally.exe", createExecutable: false);

        Assert.Null(XboxLibrary.Read(new InstalledPackage(FullName, notAGame, null)));
        Assert.Null(XboxLibrary.Read(new InstalledPackage(FullName, downloading, null)));
        Assert.Null(XboxLibrary.Read(new InstalledPackage("not-a-full-name", Package("Other", "Other.exe"), null)));
    }

    [Fact]
    public void ScanListsEachGameOnceByTitle()
    {
        var rally = Package("Starlight Rally", "StarlightRally.exe");
        var older = Package("Starlight Rally", "StarlightRally.exe");
        var harbor = Package("Harbor Lights", "Harbor.exe");

        var games = XboxLibrary.Scan([
            new InstalledPackage(FullName, rally, null),
            new InstalledPackage("Contoso.StarlightRally_1.0.11.0_x64__8wekyb3d8bbwe", older, null),
            new InstalledPackage("Fabrikam.HarborLights_2.0.0.0_x64__qwerty1234567", harbor, null),
        ]);

        Assert.Equal(["Harbor Lights", "Starlight Rally"], games.Select(g => g.Title));
        Assert.Equal(Path.Combine(rally, "StarlightRally.exe"), games[1].ExecutablePath);
    }

    [Theory]
    [InlineData("6A3F8A2B", "1782549035")]
    [InlineData("0x6a3f8a2b", "1782549035")]
    [InlineData("00000000", null)]
    [InlineData("not hex", null)]
    [InlineData(null, null)]
    public void TitleIdsAreDecimal(string? hex, string? expected) => Assert.Equal(expected, XboxLibrary.TitleId(hex));

    [Fact]
    public void TheSplashScreenIsTheKeyArtOrItsLargestQualifiedCopy()
    {
        var root = Package("Starlight Rally", "StarlightRally.exe");
        Assert.Null(XboxLibrary.Read(new InstalledPackage(FullName, root, null))!.KeyArt);

        File.WriteAllBytes(Path.Combine(root, "SplashScreen.scale-100.png"), new byte[10]);
        File.WriteAllBytes(Path.Combine(root, "SplashScreen.scale-200.png"), new byte[20]);
        Assert.Equal(Path.Combine(root, "SplashScreen.scale-200.png"), XboxLibrary.Read(new InstalledPackage(FullName, root, null))!.KeyArt);

        File.WriteAllBytes(Path.Combine(root, "SplashScreen.png"), new byte[5]);
        Assert.Equal(Path.Combine(root, "SplashScreen.png"), XboxLibrary.Read(new InstalledPackage(FullName, root, null))!.KeyArt);
        Assert.Null(XboxLibrary.Image(root, "ms-resource:Splash"));
    }

    private string Package(string displayName, string executable, string? titleId = null, bool createExecutable = true)
    {
        var root = Directory.CreateDirectory(Path.Combine(_directory, Guid.NewGuid().ToString("N"))).FullName;
        File.WriteAllText(Path.Combine(root, "MicrosoftGame.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Game configVersion="1">
              <Identity Name="Contoso.StarlightRally" Publisher="CN=Contoso" Version="1.0.12.0" />
              <ExecutableList>
                <Executable Name="Console.exe" TargetDeviceFamily="Scarlett" Id="Game" />
                <Executable Name="{executable}" TargetDeviceFamily="PC" Id="Game" />
              </ExecutableList>
              <ShellVisuals DefaultDisplayName="{displayName}" PublisherDisplayName="Contoso" SplashScreenImage="SplashScreen.png" />
              {(titleId is null ? "" : $"<TitleId>{titleId}</TitleId>")}
            </Game>
            """);
        File.WriteAllText(Path.Combine(root, "AppxManifest.xml"), Manifest("Game"));
        if (createExecutable)
        {
            var path = Path.Combine(root, executable);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }
        return root;
    }

    private static string Manifest(string appId) => $"""
        <?xml version="1.0" encoding="utf-8"?>
        <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
          <Applications>
            <Application Id="{appId}" Executable="gamelaunchhelper.exe" EntryPoint="Windows.FullTrustApplication" />
          </Applications>
        </Package>
        """;

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
