namespace Playdex.Core.Tests;

public sealed class LibraryTests
{
    private static Dictionary<string, LibraryGame> Scan(params LibraryGame[] games) => games.ToDictionary(g => g.Id);

    private static LibraryGame Steam(int appId, string title, long? lastPlayed = null) =>
        new($"steam-{appId}", title, "steam", appId, lastPlayed, null);

    [Fact]
    public void ANewerLastPlayedTimeIsTheSameGames()
    {
        var before = Scan(Steam(1145350, "Hades II", 1_000), Steam(2379780, "Balatro"));
        var after = Scan(Steam(1145350, "Hades II", 2_000), Steam(2379780, "Balatro", 3_000));

        Assert.True(Library.SameGames(before, after));
    }

    [Fact]
    public void AnInstalledOrUninstalledGameIsAChange()
    {
        var before = Scan(Steam(1145350, "Hades II"));
        var after = Scan(Steam(1145350, "Hades II"), Steam(2379780, "Balatro"));

        Assert.False(Library.SameGames(before, after));
        Assert.False(Library.SameGames(after, before));
    }

    [Fact]
    public void AnotherGameInTheSamePlaceIsAChange()
    {
        var before = Scan(Steam(1145350, "Hades II"), Steam(2379780, "Balatro"));
        var after = Scan(Steam(1145350, "Hades II"), Steam(504230, "Celeste"));

        Assert.False(Library.SameGames(before, after));
    }

    [Fact]
    public void ALaunchersGameIsListedUnderItsStoreAndRecordedAsItWouldBeOnceAdded()
    {
        var found = new StoreGame("xbox", "Contoso.Harbor_8wekyb3d8bbwe!Game", "Harbor Lights", "1782549035",
            new LaunchConfig { ExecutablePath = @"C:\XboxGames\Harbor\Content\Harbor.exe", AppUserModelId = "Contoso.Harbor_8wekyb3d8bbwe!Game" });

        var game = Library.FoundGame(found, new ApiConfig());

        Assert.Equal(("xbox-Contoso.Harbor_8wekyb3d8bbwe!Game", "Harbor Lights", "xbox"), (game.Id, game.Title, game.Source));
        Assert.Equal(("xbox", "1782549035"), (game.CaptureProvider, game.CaptureId));
        Assert.Same(found, game.Found);
    }

    [Fact]
    public void AFoundGameScannedAgainIsTheSameGameUntilItMoves()
    {
        StoreGame Epic(string executable) => new("epic", "Harbor", "Harbor Lights", "Harbor",
            new LaunchConfig { ExecutablePath = executable, Uri = "com.epicgames.launcher://apps/ns%3Aitem%3AHarbor?action=launch&silent=true" });
        var before = Scan(Library.FoundGame(Epic(@"C:\Epic\Harbor\Harbor.exe"), new ApiConfig()));
        var again = Scan(Library.FoundGame(Epic(@"C:\Epic\Harbor\Harbor.exe"), new ApiConfig()));
        var moved = Scan(Library.FoundGame(Epic(@"D:\Epic\Harbor\Harbor.exe"), new ApiConfig()));

        Assert.True(Library.SameGames(before, again));
        Assert.False(Library.SameGames(before, moved));
    }

    [Fact]
    public void NewArtworkIsAChange()
    {
        var before = Scan(Steam(1145350, "Hades II"));
        var after = Scan(Steam(1145350, "Hades II") with { Images = new SteamArt("https://steam-art.playdex/hero.jpg", null, null, null) });

        Assert.False(Library.SameGames(before, after));
    }
}
