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
    public void NewArtworkIsAChange()
    {
        var before = Scan(Steam(1145350, "Hades II"));
        var after = Scan(Steam(1145350, "Hades II") with { Images = new SteamArt("https://steam-art.playdex/hero.jpg", null, null, null) });

        Assert.False(Library.SameGames(before, after));
    }
}
