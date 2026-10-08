using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Playdex.Launcher.Host;

internal sealed record InstalledGame(int AppId, string Name, long LastPlayed);

/// <summary>
/// Installed Steam games, read from disk: the library folders in steamapps\libraryfolders.vdf and one
/// appmanifest_&lt;appid&gt;.acf per installed app. No Steam API, no network; a few small file reads.
/// </summary>
internal static partial class SteamLibrary
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
                        || values.GetValueOrDefault("name") is not { Length: > 0 } name
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

    /// <summary>Top-level "key" "value" pairs of a Valve KeyValues file; the first occurrence wins.</summary>
    private static Dictionary<string, string> KeyValues(string text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in Pair().Matches(text))
        {
            values.TryAdd(m.Groups[1].Value, m.Groups[2].Value.Replace(@"\\", @"\", StringComparison.Ordinal));
        }
        return values;
    }

    [GeneratedRegex("""^\s*"path"\s+"((?:[^"\\]|\\.)*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex PathEntry();

    [GeneratedRegex("""^\s*"([^"]+)"\s+"((?:[^"\\]|\\.)*)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex Pair();
}
