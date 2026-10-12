using System.Globalization;
using Microsoft.Win32;

namespace Playdex.Core;

/// <summary>
/// Playdex in Settings → Apps → Installed apps: the per-user uninstall entry (HKCU, no administrator needed). The setup
/// writes it and keeps a copy of itself in the app folder as the uninstaller (<see cref="SetupExecutable"/>, which
/// release archives carry too, so the self-updater keeps it); the app refreshes it at start (<see cref="Refresh"/>).
/// </summary>
public static class InstallRegistration
{
    public const string DefaultKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Playdex";
    public const string SetupExecutable = "Playdex-Setup.exe";

    public static void Register(string installDirectory, string version, string key = DefaultKey)
    {
        installDirectory = Path.GetFullPath(installDirectory).TrimEnd('\\');
        using var entry = Registry.CurrentUser.CreateSubKey(key, writable: true);
        var setup = Path.Combine(installDirectory, SetupExecutable);
        entry.SetValue("DisplayName", "Playdex");
        entry.SetValue("DisplayVersion", version);
        entry.SetValue("Publisher", "Playdex");
        entry.SetValue("DisplayIcon", $"{Path.Combine(installDirectory, AppUpdate.AppExecutable)},0");
        entry.SetValue("InstallLocation", installDirectory);
        entry.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
        entry.SetValue("UninstallString", $"\"{setup}\" --uninstall");
        entry.SetValue("QuietUninstallString", $"\"{setup}\" --uninstall --quiet");
        entry.SetValue("URLInfoAbout", "https://playdex.dev");
        entry.SetValue("NoModify", 1, RegistryValueKind.DWord);
        entry.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        entry.SetValue("EstimatedSize", SizeInKilobytes(installDirectory), RegistryValueKind.DWord);
    }

    /// <summary>
    /// Called by the app at start: the running version on this install's entry (after a self-update), or a new entry
    /// when there is none and the uninstaller is in the folder (installs that predate the entry). Another install's
    /// entry is left alone.
    /// </summary>
    public static void Refresh(string installDirectory, string version, string key = DefaultKey)
    {
        using (var entry = Registry.CurrentUser.OpenSubKey(key, writable: true))
        {
            if (entry is not null)
            {
                if (entry.GetValue("InstallLocation") is string location && SameDirectory(location, installDirectory)
                    && entry.GetValue("DisplayVersion") as string != version)
                {
                    entry.SetValue("DisplayVersion", version);
                    entry.SetValue("EstimatedSize", SizeInKilobytes(installDirectory), RegistryValueKind.DWord);
                }
                return;
            }
        }
        if (File.Exists(Path.Combine(installDirectory, SetupExecutable)))
        {
            Register(installDirectory, version, key);
        }
    }

    /// <summary>Whether the entry names <paramref name="installDirectory"/>.</summary>
    public static bool IsRegistered(string installDirectory, string key = DefaultKey) =>
        InstalledLocation(key) is { } location && SameDirectory(location, installDirectory);

    /// <summary>Where the registered install is, or null.</summary>
    public static string? InstalledLocation(string key = DefaultKey)
    {
        using var entry = Registry.CurrentUser.OpenSubKey(key);
        return entry?.GetValue("InstallLocation") as string;
    }

    public static void Unregister(string key = DefaultKey) => Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);

    public static bool SameDirectory(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    private static int SizeInKilobytes(string directory)
    {
        try
        {
            var bytes = new DirectoryInfo(directory).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            return (int)Math.Min(int.MaxValue, bytes / 1024);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
