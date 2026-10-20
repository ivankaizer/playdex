using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Playdex.Core;

public sealed record InstalledGame(int AppId, string Name, long LastPlayed);

/// <summary>Cached artwork, each a path relative to Steam's librarycache folder (forward slashes), or null.</summary>
public sealed record SteamArt(string? Hero, string? Logo, string? Portrait, string? Capsule);

/// <summary>
/// Installed Steam games, read from disk: the library folders in steamapps\libraryfolders.vdf and one
/// appmanifest_&lt;appid&gt;.acf per installed app. No Steam API, no network; a few small file reads.
/// </summary>
public static partial class SteamLibrary
{
    /// <summary>StateFlags bit 4: fully installed. Other bits (update required, downloading) still leave it launchable.</summary>
    private const int FullyInstalled = 4;

    /// <summary>Steam installs these next to games; they are not games.</summary>
    private static readonly HashSet<int> _tools =
    [
        228980, // Steamworks Common Redistributables
        250820, // SteamVR
        1070560, 1391110, 1628350, // Steam Linux Runtime
        1493710, 2180100, 1826330, // Proton
    ];

    public static List<InstalledGame> Scan()
    {
        var games = new List<InstalledGame>();
        if (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is not string steamPath)
        {
            Log.Info("steam library: Steam is not installed");
            return games;
        }
        var seen = new HashSet<int>();
        foreach (var library in LibraryFolders(steamPath))
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps))
            {
                continue;
            }
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var values = KeyValues(File.ReadAllText(manifest));
                    if (!int.TryParse(values.GetValueOrDefault("appid"), out var appId)
                        || _tools.Contains(appId)
                        || !int.TryParse(values.GetValueOrDefault("StateFlags"), out var flags)
                        || (flags & FullyInstalled) == 0
                        || GameName(values) is not { } name
                        || !seen.Add(appId))
                    {
                        continue;
                    }
                    _ = long.TryParse(values.GetValueOrDefault("LastPlayed"), out var lastPlayed);
                    games.Add(new InstalledGame(appId, name, lastPlayed));
                }
                catch (IOException ex)
                {
                    Log.Info($"steam library: {manifest} unreadable: {ex.Message}");
                }
            }
        }
        return games;
    }

    /// <summary>Steam's artwork cache (appcache\librarycache), or null without Steam.</summary>
    public static string? ArtCacheDirectory { get; } =
        Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is string steamPath
        && Path.Combine(steamPath, "appcache", "librarycache") is var dir && Directory.Exists(dir)
            ? Path.GetFullPath(dir)
            : null;

    /// <summary>
    /// The app's artwork in Steam's own cache, as paths relative to <see cref="ArtCacheDirectory"/>. Newer apps are
    /// only on the CDN under per-image hashes that can't be guessed, so this cache is the reliable source; it is also
    /// local. Files sit in librarycache\&lt;appid&gt;\ or librarycache\&lt;appid&gt;\&lt;hash&gt;\; the newest copy wins.
    /// </summary>
    public static SteamArt? FindArt(int appId)
    {
        if (ArtCacheDirectory is not { } root)
        {
            return null;
        }
        var dir = Path.Combine(root, appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!Directory.Exists(dir))
        {
            return null;
        }
        List<FileInfo> files;
        try
        {
            files = [.. new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 1 })];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"steam art: {dir} unreadable: {ex.Message}");
            return null;
        }

        string? Find(params string[] names)
        {
            foreach (var name in names)
            {
                var newest = files
                    .Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) && f.Length > 0)
                    .MaxBy(f => f.LastWriteTimeUtc);
                if (newest is not null)
                {
                    return Path.GetRelativePath(root, newest.FullName).Replace('\\', '/');
                }
            }
            return null;
        }

        var art = new SteamArt(
            Hero: Find("library_hero.jpg"),
            Logo: Find("logo.png"),
            Portrait: Find("library_600x900.jpg", "library_capsule.jpg"),
            Capsule: Find("header.jpg", "library_header.jpg"));
        return art is { Hero: null, Logo: null, Portrait: null, Capsule: null } ? null : art;
    }

    /// <summary>The app's install folder (with a trailing separator), or null when it is not installed.</summary>
    public static string? FindInstallDirectory(int appId)
    {
        if (Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") is not string steamPath)
        {
            return null;
        }
        foreach (var library in LibraryFolders(steamPath))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (File.Exists(manifest) && KeyValues(File.ReadAllText(manifest)).GetValueOrDefault("installdir") is { Length: > 0 } installDir)
            {
                return Path.GetFullPath(Path.Combine(library, "steamapps", "common", installDir)) + Path.DirectorySeparatorChar;
            }
        }
        return null;
    }

    private static IEnumerable<string> LibraryFolders(string steamPath)
    {
        yield return steamPath;
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
        {
            yield break;
        }
        foreach (Match m in PathEntry().Matches(File.ReadAllText(vdf)))
        {
            var path = m.Groups[1].Value.Replace(@"\\", @"\", StringComparison.Ordinal);
            if (!string.Equals(Path.GetFullPath(path), Path.GetFullPath(steamPath), StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    /// <summary>
    /// The manifest's name, or its install folder when the name is missing or the "appid_&lt;appid&gt;" placeholder
    /// Steam writes before it has the app's info (copied-in installs, some family-shared apps).
    /// </summary>
    internal static string? GameName(IReadOnlyDictionary<string, string> manifest)
    {
        var name = manifest.GetValueOrDefault("name");
        if (name is { Length: > 0 } && !PlaceholderName().IsMatch(name))
        {
            return name;
        }
        return manifest.GetValueOrDefault("installdir") is { Length: > 0 } installDir ? installDir : null;
    }

    /// <summary>Top-level "key" "value" pairs of a Valve KeyValues file; the first occurrence wins.</summary>
    internal static Dictionary<string, string> KeyValues(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Pair().Matches(text))
        {
            values.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Replace(@"\\", @"\", StringComparison.Ordinal));
        }
        return values;
    }

    [GeneratedRegex("""^appid_\d+$""")]
    private static partial Regex PlaceholderName();

    [GeneratedRegex("""^\s*"path"\s+"((?:[^"\\]|\\.)*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex PathEntry();

    [GeneratedRegex("""^\s*"([^"]+)"\s+"((?:[^"\\]|\\.)*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex Pair();
}
