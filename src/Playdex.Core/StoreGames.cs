namespace Playdex.Core;

/// <summary>
/// A game another launcher installed on this PC, which can be added to Playdex. <paramref name="Store"/> is the
/// launcher ("xbox", "epic", "gog") and also how it is recorded, with <paramref name="RecordedId"/> (an Xbox title id,
/// an Epic app name, a GOG game id); null when it has none. <paramref name="Id"/> is its id in the launcher, and
/// <paramref name="Launch"/> how it starts. <paramref name="KeyArt"/> is a 16:9 image of the game in its install (an
/// Xbox game's splash screen), shown until other art is picked (<see cref="StoreArt"/>); null when it has none.
/// <paramref name="InstallFolder"/> is where it is installed; null for its executable's folder.
/// </summary>
public sealed record StoreGame(
    string Store, string Id, string Title, string? RecordedId, LaunchConfig Launch, string? KeyArt = null, string? InstallFolder = null)
{
    public string ExecutablePath => Launch.ExecutablePath!;

    /// <summary>Whether its executable is started as it is, rather than by its launcher or as a packaged app.</summary>
    public bool StartsDirectly => Launch.AppUserModelId is null && Launch.Uri is null;
}

/// <summary>The games the Xbox app, the Epic Games Launcher and GOG Galaxy installed on this PC.</summary>
public static class StoreGames
{
    public const string Xbox = "xbox";
    public const string Epic = "epic";
    public const string Gog = "gog";

    public static IReadOnlyList<StoreGame> Scan() => [.. XboxLibrary.Scan(), .. EpicLibrary.Scan(), .. GogLibrary.Scan()];

    /// <summary>The installed store game <paramref name="executablePath"/> belongs to; null when it isn't one.</summary>
    public static StoreGame? FindByExecutable(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        return Scan().FirstOrDefault(g => string.Equals(g.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether <paramref name="game"/> was added from <paramref name="found"/>.</summary>
    public static bool IsAdded(GameConfig game, StoreGame found) =>
        string.Equals(game.Store, found.Store, StringComparison.OrdinalIgnoreCase)
        && string.Equals(game.StoreId, found.Id, StringComparison.OrdinalIgnoreCase);
}
