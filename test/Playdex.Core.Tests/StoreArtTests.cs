namespace Playdex.Core.Tests;

public sealed class StoreArtTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("playdex-store-art-").FullName;

    private string Root => Path.Combine(_directory, "store-art");

    [Fact]
    public void KeyArtIsCopiedOnceAndShownAsEveryKindButTheLogo()
    {
        var splash = Splash([1, 2, 3]);

        var art = StoreArt.Find(Root, "xbox-Harbor", Game(splash))!;
        var again = StoreArt.Find(Root, "xbox-Harbor", Game(splash))!;

        Assert.Equal(art, again);
        Assert.StartsWith("xbox-Harbor/keyart.", art.Hero);
        Assert.EndsWith(".png", art.Hero);
        Assert.Equal((art.Hero, art.Hero, (string?)null), (art.Capsule, art.Portrait, art.Logo));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(Root, art.Hero!)));
        Assert.Single(Directory.GetFiles(Path.Combine(Root, "xbox-Harbor")));
    }

    [Fact]
    public void ChangedKeyArtGetsANewNameAndReplacesTheOldCopy()
    {
        var splash = Splash([1, 2, 3]);
        var before = StoreArt.Find(Root, "xbox-Harbor", Game(splash))!;

        File.WriteAllBytes(splash, [4, 5, 6, 7]);
        File.SetLastWriteTimeUtc(splash, DateTime.UtcNow.AddMinutes(1));
        var after = StoreArt.Find(Root, "xbox-Harbor", Game(splash))!;

        Assert.NotEqual(before.Hero, after.Hero);
        Assert.Equal([4, 5, 6, 7], File.ReadAllBytes(Path.Combine(Root, after.Hero!)));
        Assert.Single(Directory.GetFiles(Path.Combine(Root, "xbox-Harbor")));
    }

    [Fact]
    public void NoKeyArtIsNoArt()
    {
        Assert.Null(StoreArt.Find(Root, "epic-Harbor", Game(null)));
        Assert.Null(StoreArt.Find(Root, "xbox-Harbor", Game(Path.Combine(_directory, "missing.png"))));
    }

    private string Splash(byte[] content)
    {
        var path = Path.Combine(_directory, "game", "SplashScreen.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static StoreGame Game(string? keyArt) =>
        new("xbox", "Contoso.Harbor_8wekyb3d8bbwe!Game", "Harbor Lights", "1234",
            new LaunchConfig { ExecutablePath = @"C:\XboxGames\Harbor\Content\Harbor.exe" }, keyArt);

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
