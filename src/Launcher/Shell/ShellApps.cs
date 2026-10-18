using Playdex.Core;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Shell;

/// <param name="Kind">"discord", "discord-ptb", "discord-canary", "browser" or "executable": the page picks the icon by it.</param>
/// <param name="Running">The app has a process now: opening it switches to its window.</param>
internal sealed record AppView(string Id, string Name, string Kind, bool Running);

/// <summary>The apps quick settings shows; posted to the page with type "apps".</summary>
internal sealed record AppsState(string Type, IReadOnlyList<AppView> Apps);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AppsState))]
internal sealed partial class AppsJson : JsonSerializerContext;

/// <summary>
/// Opens the apps in quick settings (<see cref="AppShortcuts"/>): switches to the app's window when it has one (a
/// browser would otherwise open another window), else starts it.
/// </summary>
internal static class ShellApps
{
    /// <summary>The apps that are on this PC, as JSON for the page. Reads the registry and the process list: off the UI thread.</summary>
    public static string ListJson(IReadOnlyList<AppShortcut> apps)
    {
        var views = new List<AppView>();
        foreach (var app in apps)
        {
            if (AppShortcuts.Resolve(app) is { } resolved)
            {
                views.Add(new AppView(app.Id, app.Name, resolved.Kind, AppShortcuts.ProcessIds(resolved).Count > 0));
            }
        }
        return JsonSerializer.Serialize(new AppsState("apps", views), AppsJson.Default.AppsState);
    }

    /// <summary>Open <paramref name="app"/>. UI thread. Returns what to tell the user when it can't be opened, else null.</summary>
    public static async Task<string?> OpenAsync(nint shell, AppShortcut app)
    {
        var resolved = await Task.Run(() => AppShortcuts.Resolve(app));
        if (resolved is null)
        {
            Log.Info($"apps: {app.Id} is not on this PC");
            return $"{app.Name} isn't installed on this PC anymore.";
        }
        var running = await Task.Run(() => AppShortcuts.ProcessIds(resolved));
        if (running.Count > 0 && GameWindows.ActivateAny(shell, running))
        {
            Log.Info($"apps: switched to {app.Id}");
            return null;
        }
        // The app (or the process that hands over to it, such as Discord's updater) may take the foreground.
        Native.AllowSetForegroundWindow(Native.AsfwAny);
        try
        {
            await Task.Run(() => AppShortcuts.Start(resolved));
        }
        catch (Win32Exception ex)
        {
            Log.Error($"apps: could not start {app.Id}", ex);
            return $"Couldn't open {app.Name}.";
        }
        return null;
    }
}
