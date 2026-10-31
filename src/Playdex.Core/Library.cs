using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

/// <summary>
/// One playable game on this PC. Source "steam": an installed Steam app. Source "playdex": a game added to Playdex (the
/// config's games), started directly by id (<see cref="ConfigGameId"/>). Source "xbox", "epic" or "gog": a game that
/// launcher installed and that isn't added, started as its <see cref="Found"/> game would be once added. <see cref="LauncherPath"/> is that game's
/// generated Steam-shortcut launcher, used only to find its Steam grid art. <see cref="CaptureProvider"/> and
/// <see cref="CaptureId"/> are how a "playdex" game is recorded to Playdex (<see cref="CaptureGame.From"/>), so the page can
/// match it to the user's Playdex games; a Steam game is recorded as ("steam", its app id). <see cref="Store"/> is the
/// launcher a game came from ("xbox", "epic", "gog"), added to Playdex or not, so the page can list it with that
/// launcher's games; null for Steam's and for one added from its executable.
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
    string? CaptureId = null,
    [property: JsonIgnore] StoreGame? Found = null,
    string? Store = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<LibraryGame>))]
public sealed partial class LibraryJson : JsonSerializerContext;

/// <summary>
/// The launcher's library: installed Steam games, games added through the Playdex Wrapper (the install's config), and the
/// Xbox app's, Epic's and GOG's games that aren't added.
/// Read from disk (a few small files) at startup and again when the window comes back, off the UI thread; the page gets
/// it before its scripts run.
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
        var found = 0;
        var storeGames = StoreGames.Scan();
        foreach (var store in storeGames.Where(f => !config.Games.Values.Any(g => StoreGames.IsAdded(g, f))))
        {
            var game = FoundGame(store, config.Api);
            if (games.TryAdd(game.Id, game))
            {
                found++;
            }
        }
        var withArt = 0;
        var customArt = CustomArt.Root(config.Storage);
        var storeArt = StoreArt.Root(config.Storage);
        foreach (var (id, game) in games.ToList())
        {
            // A launcher's game brings its own key art, added to Playdex or not.
            var store = game.Found ?? (game.ConfigGameId is { } configId && config.Games.TryGetValue(configId, out var configured)
                ? storeGames.FirstOrDefault(f => StoreGames.IsAdded(configured, f))
                : null);
            if (Images(game, customArt, store is null ? null : StoreArt.Find(storeArt, id, store)) is { } images)
            {
                games[id] = game with { Images = images };
                withArt++;
            }
        }
        Log.Info($"library: {games.Count} game(s), {wrapper.Count} added to Playdex, {found} from other launchers, {withArt} with local art");
        return games;
    }

    /// <summary>Virtual host for Steam's artwork cache (<see cref="SteamLibrary.ArtCacheDirectory"/>).</summary>
    public const string ArtHost = "steam-art.playdex";

    /// <summary>Parent of the per-user grid hosts: &lt;user id&gt;.steam-grid.playdex is that user's grid folder (<see cref="SteamGrid.GridFolders"/>).</summary>
    public const string GridHost = "steam-grid.playdex";

    /// <summary>Virtual host for artwork picked in Playdex (<see cref="CustomArt.Root"/>).</summary>
    public const string CustomArtHost = "custom-art.playdex";

    /// <summary>Virtual host for launcher games' own key art (<see cref="StoreArt.Root"/>).</summary>
    public const string StoreArtHost = "store-art.playdex";

    /// <summary>
    /// Local artwork as page URLs, per image: what the user picked in Playdex, then the game's own Steam shortcut grid art
    /// (Wrapper games, from SteamGridDB), then custom grid art on its Steam app, then a launcher game's own key art
    /// (<paramref name="storeArt"/>), then Steam's artwork cache. Kinds still missing fall back to the CDN.
    /// </summary>
    private static SteamArt? Images(LibraryGame game, string customArt, SteamArt? storeArt)
    {
        var picked = CustomArt.Find(customArt, game.Id);
        var shortcut = game.LauncherPath is { } launcher ? SteamGrid.ForShortcut(launcher) : null;
        var appGrid = game.SteamAppId is { } gridId ? SteamGrid.ForApp(gridId) : null;
        var cache = game.SteamAppId is { } cacheId ? SteamLibrary.FindArt(cacheId) : null;

        string? Pick(Func<SteamArt, string?> kind) =>
            (picked is not null ? Url(CustomArtHost, kind(picked)) : null)
            ?? (shortcut is not null ? GridUrl(kind(shortcut)) : null)
            ?? (appGrid is not null ? GridUrl(kind(appGrid)) : null)
            ?? (storeArt is not null ? Url(StoreArtHost, kind(storeArt)) : null)
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

    /// <summary>
    /// The library entry of a launcher's game that isn't added: "&lt;store&gt;-&lt;its id there&gt;", which is also the
    /// game id it is started (and locked) under. It is recorded as it would be once added.
    /// </summary>
    internal static LibraryGame FoundGame(StoreGame found, ApiConfig api)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var id = $"{found.Store}-{string.Concat(found.Id.Select(c => invalid.Contains(c) ? '_' : c))}";
        var capture = CaptureGame.From(GameCatalog.FromStore(found, id), api);
        return new LibraryGame(
            id, found.Title, found.Store, null, null, null, CaptureProvider: capture.GameIdProvider, CaptureId: capture.GameId, Found: found,
            Store: found.Store);
    }

    public static string ToJson(IReadOnlyDictionary<string, LibraryGame> games) =>
        JsonSerializer.Serialize(games.Values.ToList(), LibraryJson.Default.ListLibraryGame);

    /// <summary>
    /// Whether two scans hold the same games. A newer last-played time alone is no change: the page follows play itself.
    /// </summary>
    public static bool SameGames(IReadOnlyDictionary<string, LibraryGame> a, IReadOnlyDictionary<string, LibraryGame> b) =>
        a.Count == b.Count
        && a.All(entry => b.TryGetValue(entry.Key, out var other)
            && other with { LastPlayed = entry.Value.LastPlayed, Found = entry.Value.Found } == entry.Value
            && SameStart(entry.Value.Found, other.Found));

    /// <summary>Whether two scans start a found game the same way (an update moves an Xbox game's folder).</summary>
    private static bool SameStart(StoreGame? a, StoreGame? b) =>
        a?.Launch.ExecutablePath == b?.Launch.ExecutablePath
        && a?.InstallFolder == b?.InstallFolder
        && a?.Launch.AppUserModelId == b?.Launch.AppUserModelId
        && a?.Launch.Uri == b?.Launch.Uri
        && a?.Launch.WorkingDirectory == b?.Launch.WorkingDirectory
        && (a?.Launch.Arguments ?? []).SequenceEqual(b?.Launch.Arguments ?? []);

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
                CaptureProvider: capture.GameIdProvider, CaptureId: capture.GameId, Store: game.Store?.ToLowerInvariant()));
        }
        return games;
    }
}
