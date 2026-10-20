namespace Playdex.Core.Tests;

public sealed class SteamLibraryTests
{
    [Fact]
    public void GameNameIsTheManifestsName()
    {
        var manifest = SteamLibrary.KeyValues("""
            "AppState"
            {
            	"appid"		"620"
            	"name"		"Portal 2"
            	"installdir"		"Portal 2"
            }
            """);

        Assert.Equal("Portal 2", SteamLibrary.GameName(manifest));
    }

    [Fact]
    public void GameNameFallsBackToTheInstallFolderForSteamsPlaceholder()
    {
        var manifest = SteamLibrary.KeyValues("""
            "AppState"
            {
            	"appid"		"477160"
            	"name"		"appid_477160"
            	"installdir"		"Human Fall Flat"
            }
            """);

        Assert.Equal("Human Fall Flat", SteamLibrary.GameName(manifest));
    }

    [Fact]
    public void GameNameIsNullWithNeitherANameNorAnInstallFolder()
    {
        var manifest = SteamLibrary.KeyValues("""
            "AppState"
            {
            	"appid"		"880940"
            	"name"		"appid_880940"
            }
            """);

        Assert.Null(SteamLibrary.GameName(manifest));
    }
}
