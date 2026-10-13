using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

/// <summary>
/// One playable game on this PC. Source "steam": an installed Steam app. Source "playdex": a game added to Playdex (the
/// config's games), started directly by id (<see cref="ConfigGameId"/>). <see cref="LauncherPath"/> is that game's
/// generated Steam-shortcut launcher, used only to find its Steam grid art. <see cref="CaptureProvider"/> and
/// <see cref="CaptureId"/> are how a "playdex" game is recorded to Playdex (<see cref="CaptureGame.From"/>), so the page can
/// match it to the user's Playdex games; a Steam game is recorded as ("steam", its app id).
/// </summary>
public sealed record LibraryGame(
    string Id,
    string Title,
    string Source,
    int? SteamAppId,
    long? LastPlayed,
    [property: JsonIgnore] string? LauncherPath,
    SteamArt? Images = null,
    [property: JsonIgnore] string? ConfigGameId = null,
    string? CaptureProvider = null,
    string? CaptureId = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<LibraryGame>))]
public sealed partial class LibraryJson : JsonSerializerContext;

/// <summary>
/// The launcher's library: installed Steam games plus games added through the Playdex Wrapper (the install's config).
/// Read from disk once at startup (a few small files), off the UI thread; the page gets it before its scripts run.
/// </summary>
public static class Library
{
    public static IReadOnlyDictionary<string, LibraryGame> Scan(AppConfig config)
    {
        var games = new Dictionary<string, LibraryGame>(StringComparer.Ordinal);
        var wrapper = WrapperGames(config);
        // A game added to Playdex wins over the same Steam app: its launch settings and monitor are the user's.
        var wrapped = wrapper.Where(g => g.SteamAppId is not null).Select(g => g.SteamAppId!.Value).ToHashSet();
        foreach (var steam in SteamLibrary.Scan().Where(s => !wrapped.Contains(s.AppId)))
        {
            var id = $"steam-{steam.AppId}";
            games[id] = new LibraryGame(id, steam.Name, "steam", steam.AppId, steam.LastPlayed > 0 ? steam.LastPlayed * 1000 : null, null);
        }
        foreach (var game in wrapper)
        {
            games[game.Id] = game;
        }
        var withArt = 0;
        foreach (var (id, game) in games.ToList())
        {
            if (Images(game) is { } images)
            {
                games[id] = game with { Images = images };
                withArt++;
            }
        }
        Log.Info($"library: {games.Count} game(s), {wrapper.Count} added to Playdex, {withArt} with local art");
        return games;
    }

    /// <summary>Virtual host for Steam's artwork cache (<see cref="SteamLibrary.ArtCacheDirectory"/>).</summary>
    public const string ArtHost = "steam-art.playdex";

    /// <summary>Parent of the per-user grid hosts: &lt;user id&gt;.steam-grid.playdex is that user's grid folder (<see cref="SteamGrid.GridFolders"/>).</summary>
    public const string GridHost = "steam-grid.playdex";

    /// <summary>
    /// Local artwork as page URLs, per image: the game's own Steam shortcut grid art (Wrapper games, from SteamGridDB),
    /// then custom grid art on its Steam app, then Steam's artwork cache. Kinds still missing fall back to the CDN.
    /// </summary>
    private static SteamArt? Images(LibraryGame game)
    {
        var shortcut = game.LauncherPath is { } launcher ? SteamGrid.ForShortcut(launcher) : null;
        var appGrid = game.SteamAppId is { } gridId ? SteamGrid.ForApp(gridId) : null;
        var cache = game.SteamAppId is { } cacheId ? SteamLibrary.FindArt(cacheId) : null;

        string? Pick(Func<SteamArt, string?> kind) =>
            (shortcut is not null ? GridUrl(kind(shortcut)) : null)
            ?? (appGrid is not null ? GridUrl(kind(appGrid)) : null)
            ?? (cache is not null ? Url(ArtHost, kind(cache)) : null);

        var images = new SteamArt(Pick(a => a.Hero), Pick(a => a.Logo), Pick(a => a.Portrait), Pick(a => a.Capsule));
        return images is { Hero: null, Logo: null, Portrait: null, Capsule: null } ? null : images;
    }

    /// <summary>"&lt;user id&gt;/&lt;file&gt;" → https://&lt;user id&gt;.steam-grid.playdex/&lt;file&gt;.</summary>
    private static string? GridUrl(string? userAndFile) =>
        userAndFile?.Split('/', 2) is [var user, var file] ? Url($"{user}.{GridHost}", file) : null;

    private static string? Url(string host, string? relativePath) =>
        relativePath is null ? null : $"https://{host}/{string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString))}";

    /// <summary>The library id of a game added to Playdex.</summary>
    public static string PlaydexId(string configGameId) => $"playdex-{configGameId}";

    public static string ToJson(IReadOnlyDictionary<string, LibraryGame> games) =>
        JsonSerializer.Serialize(games.Values.ToList(), LibraryJson.Default.ListLibraryGame);

    /// <summary>
    /// The games added to Playdex whose executable is on this PC. Each is started directly; its generated launcher
    /// (the exe its Steam shortcut points at) is only looked up for Steam grid art.
    /// </summary>
    private static List<LibraryGame> WrapperGames(AppConfig config)
    {
        var games = new List<LibraryGame>();
        foreach (var game in config.Games.Values)
        {
            if (game.Launch.ExecutablePath is not { } executable || !File.Exists(executable))
            {
                continue; // Uninstalled or moved: not playable here.
            }
            var title = string.IsNullOrWhiteSpace(game.DisplayName) ? game.GameId : game.DisplayName;
            var isSteam = string.Equals(game.GameProvider, "steam", StringComparison.OrdinalIgnoreCase);
            int? steamAppId = isSteam && int.TryParse(game.ApiGameId, out var appId) ? appId : null;
            var launcher = LauncherManager.GetLauncherPath(config.Storage, game);
            var capture = CaptureGame.From(game, config.Api);
            games.Add(new LibraryGame(
                PlaydexId(game.GameId), title, "playdex", steamAppId, null, File.Exists(launcher) ? launcher : null, ConfigGameId: game.GameId,
                CaptureProvider: capture.GameIdProvider, CaptureId: capture.GameId));
        }
        return games;
    }
}
