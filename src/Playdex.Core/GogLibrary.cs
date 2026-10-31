using Microsoft.Win32;

namespace Playdex.Core;

/// <summary>
/// GOG Galaxy's installed games: one key per game under HKLM\SOFTWARE\GOG.com\Games (32-bit view) with its name,
/// executable, launch parameters and working folder. GOG games have no DRM, so a game is started directly, as an
/// executable added to Playdex is, and recorded by its GOG game id. DLCs (which depend on another game) are left out.
/// </summary>
public static class GogLibrary
{
    public static IReadOnlyList<StoreGame> Scan()
    {
        try
        {
            using var games = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (games is null)
            {
                return [];
            }
            var found = new List<StoreGame>();
            foreach (var name in games.GetSubKeyNames())
            {
                using var game = games.OpenSubKey(name);
                if (game is not null && Read(value => game.GetValue(value) as string) is { } read)
                {
                    found.Add(read);
                }
            }
            return [.. found.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("gog: the installed games could not be read", ex);
            return [];
        }
    }

    /// <summary>A game from its registry values; null for a DLC, or one that isn't (fully) installed.</summary>
    internal static StoreGame? Read(Func<string, string?> value)
    {
        string? Text(string name) => value(name) is { } text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

        if (Text("gameID") is not { } id || Text("exe") is not { } executable || Text("dependsOn") is not null)
        {
            return null;
        }
        try
        {
            var executablePath = Path.GetFullPath(executable);
            if (!File.Exists(executablePath))
            {
                return null;
            }
            var launch = new LaunchConfig
            {
                ExecutablePath = executablePath,
                Arguments = LaunchOptionsCodec.Parse(Text("launchParam")),
                WorkingDirectory = Text("workingDir") is { } folder && Directory.Exists(folder) ? Path.GetFullPath(folder) : null,
            };
            return new StoreGame(StoreGames.Gog, id, Text("gameName") ?? Path.GetFileNameWithoutExtension(executablePath), id, launch);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException or InvalidOperationException)
        {
            Log.Info($"gog: game {id} skipped ({ex.Message})");
            return null;
        }
    }
}
