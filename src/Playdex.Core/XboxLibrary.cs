using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Playdex.Core;

/// <summary>
/// A game installed through the Xbox app (a PC GDK game): the packaged app that starts it, its executable and its Xbox
/// title id in decimal, as Xbox presence (OpenXBL) reports it; null when the game has none.
/// </summary>
public sealed record XboxGame(string AppUserModelId, string Title, string ExecutablePath, string? TitleId);

/// <summary>A package registered for this user: its full name, folder and display name (often an indirect string).</summary>
internal sealed record InstalledPackage(string FullName, string RootFolder, string? DisplayName);

/// <summary>
/// The Xbox app's games on this PC: the packages registered for this user whose folder has a MicrosoftGame.config,
/// which every PC GDK game ships (C:\XboxGames\&lt;game&gt;\Content, or WindowsApps for older installs). The config
/// names the game's executable and title id; the package's AppxManifest.xml its app.
/// </summary>
public static partial class XboxLibrary
{
    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    public static IReadOnlyList<XboxGame> Scan()
    {
        try
        {
            return Scan(InstalledPackages());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Error("xbox: the installed packages could not be read", ex);
            return [];
        }
    }

    /// <summary>The installed Xbox game <paramref name="executablePath"/> belongs to; null when it isn't one.</summary>
    public static XboxGame? FindByExecutable(string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        return Scan().FirstOrDefault(g => string.Equals(g.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    internal static IReadOnlyList<XboxGame> Scan(IEnumerable<InstalledPackage> packages)
    {
        var games = new Dictionary<string, XboxGame>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            if (Read(package) is { } game)
            {
                games.TryAdd(game.AppUserModelId, game); // an older version still registered
            }
        }
        return [.. games.Values.OrderBy(g => g.Title, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The game in <paramref name="package"/>; null when it isn't an Xbox game or isn't fully installed.</summary>
    internal static XboxGame? Read(InstalledPackage package)
    {
        var gameConfig = Path.Combine(package.RootFolder, "MicrosoftGame.config");
        var manifest = Path.Combine(package.RootFolder, "AppxManifest.xml");
        if (!File.Exists(gameConfig) || !File.Exists(manifest) || FamilyName(package.FullName) is not { } family)
        {
            return null;
        }
        try
        {
            var config = XDocument.Load(gameConfig).Root;
            var executable = Elements(config, "Executable")
                .FirstOrDefault(e => e.Attribute("TargetDeviceFamily")?.Value is null or "PC" && !string.IsNullOrWhiteSpace(e.Attribute("Name")?.Value));
            if (executable is null)
            {
                return null;
            }
            var executablePath = Path.GetFullPath(Path.Combine(package.RootFolder, executable.Attribute("Name")!.Value));
            if (!File.Exists(executablePath))
            {
                return null;
            }
            // GDK packages start the game through a helper app; its id is the executable's (Game unless renamed).
            var apps = Elements(XDocument.Load(manifest).Root, "Application").Select(a => a.Attribute("Id")?.Value).OfType<string>().ToList();
            var app = apps.FirstOrDefault(id => id == (executable.Attribute("Id")?.Value ?? "Game")) ?? apps.FirstOrDefault();
            if (app is null)
            {
                return null;
            }
            var title = Literal(Elements(config, "ShellVisuals").FirstOrDefault()?.Attribute("DefaultDisplayName")?.Value)
                ?? Indirect(package.DisplayName)
                ?? package.FullName.Split('_')[0];
            return new XboxGame($"{family}!{app}", title.Trim(), executablePath, TitleId(Elements(config, "TitleId").FirstOrDefault()?.Value));
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log.Info($"xbox: {package.FullName} skipped ({ex.Message})");
            return null;
        }
    }

    /// <summary>
    /// Name_Version_Architecture_ResourceId_PublisherId → Name_PublisherId. A package name has no underscore, so the
    /// first and last parts are the family's.
    /// </summary>
    internal static string? FamilyName(string packageFullName) =>
        packageFullName.Split('_') is { Length: 5 } parts ? $"{parts[0]}_{parts[^1]}" : null;

    /// <summary>MicrosoftGame.config's hex title id ("6A3F8A2B") as Xbox presence gives it, in decimal.</summary>
    internal static string? TitleId(string? hex)
    {
        var value = hex?.Trim();
        if (value?.StartsWith("0x", StringComparison.OrdinalIgnoreCase) == true)
        {
            value = value[2..];
        }
        return uint.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var id) && id != 0
            ? id.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static IEnumerable<XElement> Elements(XElement? root, string name) =>
        root?.DescendantsAndSelf().Where(e => e.Name.LocalName == name) ?? [];

    /// <summary>A display name as written; null for a resource reference ("ms-resource:…") or none.</summary>
    private static string? Literal(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) ? null : name;

    /// <summary>A registered display name, with an indirect one ("@{package?ms-resource://…}") looked up.</summary>
    private static unsafe string? Indirect(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith('@'))
        {
            return Literal(name);
        }
        var buffer = stackalloc char[512];
        return SHLoadIndirectString(name, buffer, 512, 0) >= 0 ? Literal(new string(buffer)) : null;
    }

    private static IEnumerable<InstalledPackage> InstalledPackages()
    {
        using var packages = Registry.CurrentUser.OpenSubKey(PackagesKey);
        if (packages is null)
        {
            yield break;
        }
        foreach (var fullName in packages.GetSubKeyNames())
        {
            using var package = packages.OpenSubKey(fullName);
            if (package?.GetValue("PackageRootFolder") is string root && !string.IsNullOrWhiteSpace(root))
            {
                yield return new InstalledPackage(fullName, root, package.GetValue("DisplayName") as string);
            }
        }
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int SHLoadIndirectString(string source, char* output, uint outputLength, nint reserved);
}
