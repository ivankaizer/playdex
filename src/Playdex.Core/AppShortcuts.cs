using Microsoft.Win32;
using System.Diagnostics;

namespace Playdex.Core;

/// <summary>
/// An app as found on this PC, ready to start. <see cref="ProcessDirectory"/> and <see cref="ProcessName"/> say which
/// processes are the app once it runs (Discord's updater starts Discord.exe from a versioned folder under its own).
/// </summary>
public sealed record ResolvedApp(
    string Id,
    string Name,
    string Kind,
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    string ProcessDirectory,
    string ProcessName);

/// <summary>
/// The apps Big Picture opens from quick settings, for the Full Screen Experience, where there is no taskbar or Start
/// menu: Discord (and its PTB and Canary builds) and the default browser when they are on this PC, plus any executable
/// the user adds. Not games: nothing is recorded to Playdex. Until the user changes the list
/// (<see cref="AppConfig.Apps"/> is null) it is the apps found here, so an app installed later shows up by itself; the
/// first change saves the list as it is then.
/// </summary>
public static class AppShortcuts
{
    public const string ExecutableKind = "executable";
    public const string BrowserKind = "browser";
    public const int MaximumNameLength = 64;

    /// <summary>Discord's builds: each installs its updater in %LOCALAPPDATA%\&lt;folder&gt; and runs &lt;exe&gt; from app-&lt;version&gt;.</summary>
    private static readonly (string Kind, string Name, string Folder, string Exe)[] _discord =
    [
        ("discord", "Discord", "Discord", "Discord.exe"),
        ("discord-ptb", "Discord PTB", "DiscordPTB", "DiscordPTB.exe"),
        ("discord-canary", "Discord Canary", "DiscordCanary", "DiscordCanary.exe"),
    ];

    /// <summary>The kinds Playdex finds by itself.</summary>
    public static IEnumerable<string> Kinds => _discord.Select(d => d.Kind).Append(BrowserKind);

    /// <summary>The apps to show: the user's list, or until there is one, the apps found on this PC.</summary>
    public static IReadOnlyList<AppShortcut> List(AppConfig config) => config.Apps ?? Detected();

    /// <summary>The known apps installed on this PC, in the order a new list has them.</summary>
    public static List<AppShortcut> Detected() => Detected(LocalAppData, DefaultBrowser);

    internal static List<AppShortcut> Detected(string localAppData, Func<ResolvedApp?> browser)
    {
        var apps = new List<AppShortcut>();
        foreach (var (kind, name, folder, _) in _discord)
        {
            if (File.Exists(Path.Combine(localAppData, folder, "Update.exe")))
            {
                apps.Add(new AppShortcut { Id = kind, Name = name, Kind = kind });
            }
        }
        if (browser() is { } found)
        {
            apps.Add(new AppShortcut { Id = BrowserKind, Name = found.Name, Kind = BrowserKind });
        }
        return apps;
    }

    /// <summary>Known apps found here that aren't in the list: what Manage offers to add.</summary>
    public static List<AppShortcut> Suggestions(AppConfig config)
    {
        var listed = List(config).Select(a => a.Kind).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Detected().Where(a => !listed.Contains(a.Kind)).ToList();
    }

    /// <summary>The app as it is on this PC now; null when it isn't (uninstalled, moved, no browser).</summary>
    public static ResolvedApp? Resolve(AppShortcut app) => Resolve(app, LocalAppData, DefaultBrowser);

    internal static ResolvedApp? Resolve(AppShortcut app, string localAppData, Func<ResolvedApp?> browser)
    {
        if (string.Equals(app.Kind, BrowserKind, StringComparison.OrdinalIgnoreCase))
        {
            return browser() is { } found ? found with { Id = app.Id, Name = app.Name, Arguments = [.. app.Arguments] } : null;
        }
        var known = Array.FindIndex(_discord, d => string.Equals(d.Kind, app.Kind, StringComparison.OrdinalIgnoreCase));
        if (known >= 0)
        {
            var discord = _discord[known];
            var folder = Path.Combine(localAppData, discord.Folder);
            var updater = Path.Combine(folder, "Update.exe");
            // The updater starts the newest version (and updates it), as Discord's own Start menu shortcut does.
            return File.Exists(updater)
                ? new ResolvedApp(app.Id, app.Name, discord.Kind, updater, ["--processStart", discord.Exe, .. app.Arguments], folder, Path.GetFileNameWithoutExtension(discord.Exe))
                : null;
        }
        if (app.ExecutablePath is not { } path || !File.Exists(path))
        {
            return null;
        }
        return new ResolvedApp(app.Id, app.Name, ExecutableKind, path, [.. app.Arguments], Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Start the app. Its window comes up in front of Playdex's, which is in front when this is asked.</summary>
    public static void Start(ResolvedApp app)
    {
        var start = new ProcessStartInfo(app.ExecutablePath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(app.ExecutablePath),
        };
        foreach (var argument in app.Arguments)
        {
            start.ArgumentList.Add(argument);
        }
        Process.Start(start)?.Dispose();
        Log.Info($"apps: started {app.Id} ({app.ExecutablePath})");
    }

    /// <summary>The app's running processes (by name first; only those are opened to read their path).</summary>
    public static List<int> ProcessIds(ResolvedApp app)
    {
        var root = app.ProcessDirectory.TrimEnd('\\') + "\\";
        var processes = Process.GetProcessesByName(app.ProcessName);
        try
        {
            return processes
                .Where(p => ProcessImage.PathOf(p.Id) is { } path && path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Id)
                .ToList();
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>Add an executable the user picked. Named after its file description ("Spotify"), else its file name.</summary>
    public static AppShortcut AddExecutable(AppConfig config, string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath) || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Select an existing .exe file.");
        }
        var apps = Editable(config);
        if (apps.Any(a => string.Equals(a.ExecutablePath, fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"{Path.GetFileName(fullPath)} is already in your apps.");
        }
        var description = FileVersionInfo.GetVersionInfo(fullPath).FileDescription?.Trim();
        var name = string.IsNullOrWhiteSpace(description) || description.Length > MaximumNameLength
            ? Path.GetFileNameWithoutExtension(fullPath)
            : description;
        var app = new AppShortcut
        {
            Id = UniqueId(apps, Path.GetFileNameWithoutExtension(fullPath)),
            Name = name,
            Kind = ExecutableKind,
            ExecutablePath = fullPath,
        };
        apps.Add(app);
        return app;
    }

    /// <summary>Add a known app found on this PC (one of <see cref="Suggestions"/>).</summary>
    public static AppShortcut AddDetected(AppConfig config, string kind)
    {
        var apps = Editable(config);
        if (apps.Any(a => string.Equals(a.Kind, kind, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("That app is already in your apps.");
        }
        var found = Detected().FirstOrDefault(a => string.Equals(a.Kind, kind, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("That app isn't installed on this PC.");
        found.Id = UniqueId(apps, found.Kind);
        apps.Add(found);
        return found;
    }

    public static void Change(AppConfig config, string id, string? name, string? launchOptions)
    {
        var app = Find(Editable(config), id);
        app.Name = NormalizeName(name);
        app.Arguments = LaunchOptionsCodec.Parse(launchOptions);
    }

    public static void Remove(AppConfig config, string id)
    {
        var apps = Editable(config);
        apps.Remove(Find(apps, id));
    }

    public static string NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("App name cannot be empty.");
        }
        if (normalized.Length > MaximumNameLength)
        {
            throw new InvalidOperationException($"App name cannot exceed {MaximumNameLength} characters.");
        }
        return normalized;
    }

    /// <summary>A saved list as the config store keeps it: no empty entries, unique ids, a name for each.</summary>
    internal static List<AppShortcut> Normalize(List<AppShortcut> apps)
    {
        var result = new List<AppShortcut>();
        foreach (var app in apps)
        {
            if (app is null)
            {
                continue;
            }
            app.Kind = string.IsNullOrWhiteSpace(app.Kind) ? ExecutableKind : app.Kind.Trim().ToLowerInvariant();
            app.ExecutablePath = string.IsNullOrWhiteSpace(app.ExecutablePath) ? null : app.ExecutablePath.Trim();
            if (app.Kind == ExecutableKind && app.ExecutablePath is null)
            {
                continue;
            }
            app.Arguments ??= [];
            app.Name = string.IsNullOrWhiteSpace(app.Name)
                ? app.ExecutablePath is { } path ? Path.GetFileNameWithoutExtension(path) : app.Kind
                : app.Name.Trim();
            app.Id = UniqueId(result, string.IsNullOrWhiteSpace(app.Id) ? app.Kind : app.Id.Trim());
            result.Add(app);
        }
        return result;
    }

    /// <summary>The list to change: the first change saves the apps found here as the user's list.</summary>
    private static List<AppShortcut> Editable(AppConfig config) => config.Apps ??= Detected();

    private static AppShortcut Find(List<AppShortcut> apps, string id) =>
        apps.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("That app isn't in your apps anymore.");

    private static string UniqueId(IEnumerable<AppShortcut> apps, string wanted)
    {
        var baseId = new string(wanted.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (baseId.Length == 0)
        {
            baseId = "app";
        }
        var taken = apps.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = baseId;
        for (var n = 2; taken.Contains(id); n++)
        {
            id = $"{baseId}-{n}";
        }
        return id;
    }

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// The default browser: the app chosen for https links (Settings → Apps → Default apps), its executable from the
    /// association's open command. Named as the browser registers itself, else by its file description.
    /// </summary>
    private static ResolvedApp? DefaultBrowser()
    {
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (choice?.GetValue("ProgId") is not string progId || string.IsNullOrWhiteSpace(progId))
            {
                return null;
            }
            using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            if (ExecutableOfCommand(command?.GetValue(null) as string) is not { } path || !File.Exists(path))
            {
                return null;
            }
            using var application = Registry.ClassesRoot.OpenSubKey($@"{progId}\Application");
            var registered = application?.GetValue("ApplicationName") as string;
            var name = !string.IsNullOrWhiteSpace(registered) && !registered.StartsWith('@')
                ? registered.Trim()
                : FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim() is { Length: > 0 } description ? description : "Browser";
            return new ResolvedApp(BrowserKind, name, BrowserKind, path, [], Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            Log.Error("apps: default browser unreadable", ex);
            return null;
        }
    }

    /// <summary>The executable of a shell command line: "C:\…\msedge.exe" --single-argument %1 → C:\…\msedge.exe.</summary>
    internal static string? ExecutableOfCommand(string? command)
    {
        var text = command?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            return end > 1 ? Environment.ExpandEnvironmentVariables(text[1..end]) : null;
        }
        var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? Environment.ExpandEnvironmentVariables(text[..(exe + 4)]) : null;
    }
}
