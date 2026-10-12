using System.Diagnostics;
using System.Runtime.InteropServices;
using Playdex.Core;

namespace Playdex.Setup;

/// <summary>
/// Installs the Playdex desktop app for this user into %LOCALAPPDATA%\Programs\Playdex: downloads
/// playdex-windows-v&lt;version&gt;.zip from the GitHub release this exe was built for (its .sha256 checked), extracts it,
/// adds a Start menu shortcut and starts Playdex. Once Playdex is installed, running this again just starts it.
/// </summary>
/// <remarks>
/// <para>A fresh install without an API key in the install's config asks for the Playdex server and API key first, in
/// a small window that checks the key with the server and then shows the install running (<see cref="SetupWindow"/>).
/// Skip installs without one; Playdex → Settings takes it later.</para>
/// </remarks>
/// <remarks>
/// <para>It is also the bridge from the WinForms Wrapper. The last Wrapper release ships this exe as Wrapper.exe; the
/// Wrapper's updater swaps it in and starts it with <c>--update-health &lt;path&gt;</c>, waiting only 30 seconds for that
/// file, so it is written first and the download follows. From then on Wrapper.exe opens Playdex. The desktop app takes
/// over the Wrapper's config (migrated on first start), backups and generated launchers (regenerated from
/// Playdex.Run.exe), so Steam shortcuts keep working.</para>
/// <para>Flags: --reinstall installs even when Playdex is already there (Playdex must not be running). For testing:
/// --archive=PATH installs from a local zip instead of the release, --install-dir=PATH installs elsewhere, --no-shortcut
/// and --no-start skip the Start menu shortcut and starting Playdex. --api-url=URL and --api-key=KEY save the server and key
/// without asking (scripted installs); --no-prompt installs without asking, --prompt asks even when a key is saved.</para>
/// </remarks>
internal static partial class Program
{
    private static string _install = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playdex");

    [STAThread]
    private static int Main(string[] args)
    {
        Log.UseFile(Path.Combine(Paths.DataDirectory, "setup.log"));
        var updater = false;
        if (args is ["--update-health", var health, ..])
        {
            // The Wrapper's updater rolls back unless this appears within 30 s; the download takes longer.
            Confirm(health);
            updater = true;
        }
        string? Value(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];
        _install = Value("install-dir") is { } directory ? Path.GetFullPath(directory) : _install;
        var start = !args.Contains("--no-start");
        var executable = Path.Combine(_install, AppUpdate.AppExecutable);
        var apiUrl = Value("api-url");
        var apiKey = Value("api-key");
        if (apiUrl is not null || apiKey is not null)
        {
            SaveApi(apiUrl, apiKey);
        }
        if (File.Exists(executable) && !args.Contains("--reinstall"))
        {
            Log.Info("setup: Playdex is installed; starting it");
            if (start)
            {
                Start(executable);
            }
            return 0;
        }

        try
        {
            async Task Install()
            {
                await InstallAsync(Value("archive"));
                if (!args.Contains("--no-shortcut"))
                {
                    CreateStartMenuShortcut(executable);
                }
            }

            // The Wrapper's updater runs unattended, and its users bring their config (and key) with them.
            if (args.Contains("--prompt") || (!updater && !args.Contains("--no-prompt") && !HasApiKey()))
            {
                var (outcome, failure) = SetupWindow.Run(BaseUrl(), CheckKeyAsync, SaveApi, Install);
                if (outcome == SetupOutcome.Cancelled)
                {
                    return 1;
                }
                if (failure is not null)
                {
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
                }
            }
            else
            {
                Install().GetAwaiter().GetResult();
            }
            Log.Info($"setup: installed Playdex {Version} into {_install}");
            if (start)
            {
                Start(executable);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("setup: install failed", ex);
            MessageBox(0, $"Playdex could not be installed.\n\n{ex.Message}\n\nDetails are in {Path.Combine(Paths.DataDirectory, "setup.log")}.", "Playdex", 0x10);
            return 1;
        }
    }

    private static bool HasApiKey() => !string.IsNullOrWhiteSpace(ConfigStore.Default().LoadOrCreate().Api.ApiKey);

    private static string BaseUrl() =>
        ConfigStore.Default().LoadOrCreate().Api.BaseUrl is { Length: > 0 } url ? url : ApiConfig.DefaultBaseUrl;

    private static async Task<ApiKeyCheck> CheckKeyAsync(string baseUrl, string apiKey)
    {
        using var client = new CaptureClient(new ApiConfig { BaseUrl = baseUrl, ApiKey = apiKey });
        return await client.CheckKeyAsync();
    }

    /// <summary>The server and key into the install's config, which Playdex reads at start. Null leaves one as it is.</summary>
    private static void SaveApi(string? baseUrl, string? apiKey)
    {
        var store = ConfigStore.Default();
        var config = store.LoadOrCreate();
        if (baseUrl is not null)
        {
            config.Api.BaseUrl = ApiConfig.NormalizeBaseUrl(baseUrl);
        }
        if (apiKey is not null)
        {
            config.Api.ApiKey = apiKey.Trim();
        }
        store.Save(config);
        Log.Info($"setup: saved the API settings (API {config.Api.BaseUrl}, key {(string.IsNullOrWhiteSpace(config.Api.ApiKey) ? "missing" : "set")})");
    }

    private static string Version =>
        typeof(Program).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";

    private static async Task InstallAsync(string? archive)
    {
        var staging = Path.Combine(AppUpdate.UpdateRoot, $"setup-{Guid.NewGuid():N}");
        try
        {
            string app;
            if (archive is not null)
            {
                app = Path.Combine(staging, "app");
                AppUpdate.Extract(Path.GetFullPath(archive), app);
            }
            else
            {
                if (!ReleaseVersion.TryParseTag($"v{Version}", out var version))
                {
                    throw new InvalidOperationException($"This setup has no release version ({Version}).");
                }
                using var releases = new GitHubReleases();
                var release = await releases.ForVersionAsync(version)
                    ?? throw new InvalidOperationException($"Playdex {version} is not on GitHub Releases.");
                var manifest = AppUpdate.Load(await AppUpdate.StageAsync(releases, release, _install, staging));
                app = manifest.StagedDirectory;
            }

            if (Directory.Exists(_install))
            {
                Directory.Delete(_install, recursive: true);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_install)!);
            Directory.Move(app, _install);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Info($"setup: could not remove {staging}: {ex.Message}");
            }
        }
    }

    private static void Confirm(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "healthy");
            Log.Info("setup: started by the Wrapper's updater; confirmed");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Error("setup: could not confirm to the Wrapper's updater", ex);
        }
    }

    private static void Start(string executable) =>
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) })?.Dispose();

    /// <summary>Start menu → Playdex, through Windows Script Host's shortcut object (via PowerShell, no COM interop here).</summary>
    private static void CreateStartMenuShortcut(string executable)
    {
        var shortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Playdex.lnk");
        static string Quote(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
        var script = $"$s = (New-Object -ComObject WScript.Shell).CreateShortcut({Quote(shortcut)}); $s.TargetPath = {Quote(executable)}; " +
                     $"$s.WorkingDirectory = {Quote(Path.GetDirectoryName(executable)!)}; $s.Description = 'Playdex'; $s.Save()";
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script })
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using var process = Process.Start(start);
            process?.WaitForExit(30_000);
            Log.Info(process?.ExitCode == 0 ? $"setup: Start menu shortcut {shortcut}" : "setup: the Start menu shortcut was not created");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Log.Error("setup: the Start menu shortcut was not created", ex);
        }
    }

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(nint owner, string text, string caption, uint type);
}
