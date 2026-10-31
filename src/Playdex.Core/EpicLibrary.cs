using System.Text.Json;
using Microsoft.Win32;

namespace Playdex.Core;

/// <summary>
/// The Epic Games Launcher's installed games: one JSON manifest (*.item) per install in its Manifests folder. A game is
/// started through the launcher (most need it to sign in), by the link it registers, and recorded by its app name.
/// Add-ons (another main game), engine plugins and unfinished installs are left out.
/// </summary>
public static class EpicLibrary
{
    public static IReadOnlyList<StoreGame> Scan()
    {
        try
        {
            return ManifestFolder() is { } folder ? Scan(folder) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("epic: the installed games could not be read", ex);
            return [];
        }
    }

    internal static IReadOnlyList<StoreGame> Scan(string manifestFolder)
    {
        if (!Directory.Exists(manifestFolder))
        {
            return [];
        }
        var games = new Dictionary<string, StoreGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in Directory.EnumerateFiles(manifestFolder, "*.item"))
        {
            if (Read(manifest) is { } game)
            {
                games.TryAdd(game.Id, game);
            }
        }
        return [.. games.Values.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    internal static StoreGame? Read(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = document.RootElement;
            string? Text(string name) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
                    ? value.GetString()!.Trim()
                    : null;

            if (Text("AppName") is not { } appName
                || Text("InstallLocation") is not { } folder
                || Text("LaunchExecutable") is not { } executable
                || Text("CatalogNamespace") is not { } catalogNamespace
                || Text("CatalogItemId") is not { } catalogItem
                || root.TryGetProperty("bIsIncompleteInstall", out var incomplete) && incomplete.ValueKind == JsonValueKind.True
                || Text("MainGameAppName") is { } main && !string.Equals(main, appName, StringComparison.OrdinalIgnoreCase)
                || !IsGame(root))
            {
                return null;
            }
            var executablePath = Path.GetFullPath(Path.Combine(folder, executable));
            if (!File.Exists(executablePath))
            {
                return null;
            }
            var launch = new LaunchConfig { ExecutablePath = executablePath, Uri = LaunchUri(catalogNamespace, catalogItem, appName) };
            return new StoreGame(StoreGames.Epic, appName, Text("DisplayName") ?? appName, appName, launch, InstallFolder: Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Info($"epic: {Path.GetFileName(manifestPath)} skipped ({ex.Message})");
            return null;
        }
    }

    /// <summary>The link that has the Epic Games Launcher start a game, without showing its window.</summary>
    internal static string LaunchUri(string catalogNamespace, string catalogItem, string appName) =>
        $"com.epicgames.launcher://apps/{Uri.EscapeDataString(catalogNamespace)}%3A{Uri.EscapeDataString(catalogItem)}%3A{Uri.EscapeDataString(appName)}?action=launch&silent=true";

    /// <summary>A game by its categories ("public", "games", "applications"); engine plugins aren't. None listed counts.</summary>
    private static bool IsGame(JsonElement root)
    {
        if (!root.TryGetProperty("AppCategories", out var categories) || categories.ValueKind != JsonValueKind.Array)
        {
            return true;
        }
        var names = categories.EnumerateArray().Select(c => c.ValueKind == JsonValueKind.String ? c.GetString() : null).ToList();
        return names.Contains("games", StringComparer.OrdinalIgnoreCase)
            && !names.Any(n => n?.StartsWith("plugins", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>Where the launcher keeps its manifests: under its AppDataPath, or the default under ProgramData.</summary>
    private static string? ManifestFolder()
    {
        using var launcher = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
            .OpenSubKey(@"SOFTWARE\Epic Games\EpicGamesLauncher");
        var data = launcher?.GetValue("AppDataPath") as string;
        return string.IsNullOrWhiteSpace(data)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests")
            : Path.Combine(data, "Manifests");
    }
}
