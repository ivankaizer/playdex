using System.Diagnostics;
using System.Runtime.InteropServices;
using Playdex.Core;

namespace Playdex.Setup;

/// <summary>
/// Installs Playdex for this user into %LOCALAPPDATA%\Programs\Playdex, updates an older install in place, and
/// uninstalls it (<see cref="Uninstaller"/>). The app comes from the setup's own payload (the offline setup a release
/// ships, see <see cref="SetupPayload"/>), else from playdex-windows-v&lt;version&gt;.zip of the release this exe was
/// built for on download.playdex.dev. It adds a Start menu shortcut and the Settings → Apps entry (with itself as the
/// uninstaller), can set up the Full Screen Experience package, and starts Playdex. When the same or a newer Playdex is installed, running it
/// just starts Playdex.
/// </summary>
/// <remarks>
/// <para>Run interactively it shows a window first (<see cref="SetupWindow"/>): the Playdex server and API key (checked
/// with the server; a saved key is kept when the field is left empty), the Full Screen Experience option when Developer
/// Mode is on, then the install's progress. A running Playdex is asked to quit first.</para>
/// <para>It is also the bridge from the WinForms Wrapper. The last Wrapper release ships this exe as Wrapper.exe; the
/// Wrapper's updater swaps it in and starts it with <c>--update-health &lt;path&gt;</c>, waiting only 30 seconds for that
/// file, so it is written first and the download follows, without a window. From then on Wrapper.exe opens Playdex. The
/// app takes over the Wrapper's config (migrated on first start), backups and generated launchers (regenerated from
/// Playdex.Run.exe), so Steam shortcuts keep working.</para>
/// <para>Flags: --reinstall installs over the same version. --api-url=URL and --api-key=KEY save the server and key;
/// --quiet (or --no-prompt) installs without the window. --uninstall [--quiet] removes Playdex. --bundle --app=DIR
/// [--fse=FILE ...] --out=FILE writes the offline setup: this exe with the app folder and the FSE package appended.
/// For testing: --archive=PATH installs from a local app zip, --install-dir=PATH installs elsewhere, --no-shortcut,
/// --no-register and --no-start skip the Start menu shortcut, the Settings → Apps entry and starting Playdex.</para>
/// </remarks>
internal static partial class Program
{
    /// <summary>The first version that quits when started with --quit; older ones are asked to by the user.</summary>
    private static readonly ReleaseVersion _quitsOnRequest = new(2, 0, 0);

    // Before _install: static fields initialize in order.
    public static string DefaultInstallDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Playdex");

    private static string _install = DefaultInstallDirectory;
    private static string? _warning;

    [STAThread]
    private static int Main(string[] args)
    {
        Log.UseFile(Path.Combine(Paths.DataDirectory, "setup.log"));
        string? Value(string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..];
        if (args.Contains("--bundle"))
        {
            return Bundle(Value("app"), args.Where(a => a.StartsWith("--fse=", StringComparison.Ordinal)).Select(a => a["--fse=".Length..]), Value("out"));
        }
        _install = Value("install-dir") is { } directory ? Path.GetFullPath(directory) : _install;
        if (args.Contains("--uninstall"))
        {
            return Uninstaller.Run(args, Value("install-dir") is not null ? _install : null);
        }

        var updater = false;
        if (args is ["--update-health", var health, ..])
        {
            // The Wrapper's updater rolls back unless this appears within 30 s; the download takes longer.
            Confirm(health);
            updater = true;
        }
        var start = !args.Contains("--no-start");
        var executable = Path.Combine(_install, AppUpdate.AppExecutable);
        var apiUrl = Value("api-url");
        var apiKey = Value("api-key");
        if (apiUrl is not null || apiKey is not null)
        {
            SaveApi(apiUrl, apiKey);
        }
        var installed = InstalledVersion(executable);
        if (installed is { } existing && !args.Contains("--reinstall") && existing >= ThisVersion)
        {
            Log.Info($"setup: Playdex {existing} is installed; starting it");
            if (start)
            {
                Start(executable);
            }
            return 0;
        }
        if (IsInside(Environment.ProcessPath, _install))
        {
            return RelaunchFromTemp(args, _install); // the folder this exe is in gets replaced
        }

        try
        {
            var interactive = !updater && !args.Contains("--quiet") && !args.Contains("--no-prompt");
            var offerFse = interactive ? FsePackage.DeveloperModeOn() : false;
            var fseInstalled = FsePackage.DeveloperModeOn() && IsFseInstalled();
            Task Install(bool fse, Action<string> report) => InstallAsync(
                executable, installed, Value("archive"), fse, report,
                shortcut: !args.Contains("--no-shortcut"), register: !args.Contains("--no-register"));

            if (interactive)
            {
                var (outcome, failure) = SetupWindow.Run(Page(installed, offerFse, fseInstalled), CheckKeyAsync, SaveApi, Install);
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
                // Unattended: an installed FSE package is updated along with the app when this setup carries one.
                Install(fseInstalled && HasPayloadFse(), text => Log.Info($"setup: {text}")).GetAwaiter().GetResult();
            }
            Log.Info($"setup: installed Playdex {ThisVersion} into {_install}");
            if (_warning is not null && interactive)
            {
                MessageBox(0, _warning, "Playdex Setup", 0x30);
            }
            if (start)
            {
                Start(executable);
            }
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("setup: install failed", ex);
            MessageBox(0, $"Playdex could not be installed.\n\n{ex.Message}\n\nDetails are in {Path.Combine(Paths.DataDirectory, "setup.log")}.", "Playdex Setup", 0x10);
            return 1;
        }
        finally
        {
            DeleteSelfLater(args);
        }
    }

    private static ReleaseVersion ThisVersion =>
        typeof(Program).Assembly.GetName().Version is { } v ? new ReleaseVersion(v.Major, v.Minor, v.Build) : default;

    /// <summary>The version of an installed Playdex.exe (its product version), or null.</summary>
    public static ReleaseVersion? InstalledVersion(string executable)
    {
        if (!File.Exists(executable))
        {
            return null;
        }
        var product = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "";
        return ReleaseVersion.TryParseTag($"v{product.Split('+')[0]}", out var version) ? version : new ReleaseVersion(0, 0, 0);
    }

    private static SetupPage Page(ReleaseVersion? installed, bool offerFse, bool fseInstalled)
    {
        var keySaved = HasApiKey();
        var (heading, body, label) = installed switch
        {
            { } old when old < ThisVersion => (
                $"Update Playdex to {ThisVersion}",
                $"Playdex {old} is installed. It is closed and updated; your games, settings and API key stay as they are.",
                "Update"),
            not null => (
                $"Reinstall Playdex {ThisVersion}",
                "Playdex is installed again over itself; your games, settings and API key stay as they are.",
                "Reinstall"),
            null when keySaved => (
                $"Install Playdex {ThisVersion}",
                "Your Playdex account is already set up on this PC. Change the server or key below, or leave them as they are.",
                "Install"),
            null => (
                "Connect Playdex to your account",
                "Playdex records the games you play on this PC with your API key. Create one on the Playdex website under Integrations, then paste it here.",
                "Install"),
        };
        return new SetupPage(heading, body, BaseUrl(), keySaved, offerFse, fseInstalled, label);
    }

    private static async Task InstallAsync(
        string executable, ReleaseVersion? installed, string? archive, bool fse, Action<string> report, bool shortcut, bool register)
    {
        var staging = Path.Combine(AppUpdate.UpdateRoot, $"setup-{Guid.NewGuid():N}");
        try
        {
            if (installed is { } running)
            {
                await ClosePlaydexAsync(executable, running, report);
            }

            report("Installing Playdex…");
            string app;
            string? fseFolder = null;
            if (archive is not null)
            {
                app = Path.Combine(staging, "app");
                AppUpdate.Extract(Path.GetFullPath(archive), app);
            }
            else if (SetupPayload.TryExtract(Environment.ProcessPath!, staging))
            {
                app = Path.Combine(staging, SetupPayload.AppFolder);
                fseFolder = Path.Combine(staging, SetupPayload.FseFolder);
            }
            else
            {
                report("Downloading Playdex…");
                using var releases = new ReleaseFeed();
                var release = await releases.ForVersionAsync(ThisVersion)
                    ?? throw new InvalidOperationException($"Playdex {ThisVersion} is not on download.playdex.dev.");
                var manifest = AppUpdate.Load(await AppUpdate.StageAsync(releases, release, _install, staging));
                app = manifest.StagedDirectory;
                report("Installing Playdex…");
            }

            await ReplaceFolderAsync(app, _install);
            var uninstaller = Path.Combine(_install, InstallRegistration.SetupExecutable);
            if (!File.Exists(uninstaller))
            {
                // Release archives carry it; local ones may not.
                SetupPayload.CopyExecutable(Environment.ProcessPath!, uninstaller);
            }
            if (shortcut)
            {
                CreateStartMenuShortcut(executable);
            }
            if (register)
            {
                InstallRegistration.Register(_install, ThisVersion.ToString());
            }

            if (fse)
            {
                report("Setting up the Full Screen Experience… (Windows may ask for permission)");
                try
                {
                    await InstallFseAsync(fseFolder, staging);
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or HttpRequestException
                    or System.ComponentModel.Win32Exception or System.Security.Cryptography.CryptographicException)
                {
                    // The app is installed; the package can be added later from Playdex → Settings.
                    Log.Error("setup: the Full Screen Experience package was not installed", ex);
                    _warning = $"Playdex is installed, but the Full Screen Experience package was not: {ex.Message}\n\nYou can add it later in Playdex → Settings → This app.";
                }
            }
            report("Done.");
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

    /// <summary>Swap <paramref name="install"/> for <paramref name="app"/>; files of a just-closed Playdex can take a moment to free.</summary>
    private static async Task ReplaceFolderAsync(string app, string install)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(install))
                {
                    Directory.Delete(install, recursive: true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(install)!);
                Directory.Move(app, install);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 20)
            {
                Log.Info($"setup: {install} is busy ({ex.Message}); retrying");
                await Task.Delay(500);
            }
        }
    }

    /// <summary>The package from this setup's payload, else from the release.</summary>
    private static async Task InstallFseAsync(string? fseFolder, string staging)
    {
        var msix = fseFolder is not null && Directory.Exists(fseFolder) ? Directory.EnumerateFiles(fseFolder, "*.msix").FirstOrDefault() : null;
        var cer = fseFolder is not null && Directory.Exists(fseFolder) ? Directory.EnumerateFiles(fseFolder, "*.cer").FirstOrDefault() : null;
        if (msix is null || cer is null)
        {
            using var releases = new ReleaseFeed();
            var release = await releases.ForVersionAsync(ThisVersion)
                ?? throw new InvalidOperationException($"Playdex {ThisVersion} is not on download.playdex.dev.");
            var msixAsset = release.Asset($"playdex-launcher-{ThisVersion.Tag}.msix");
            var cerAsset = release.Asset("playdex-launcher.cer");
            if (msixAsset is null || cerAsset is null)
            {
                throw new InvalidOperationException($"Playdex {ThisVersion} has no signed Full Screen Experience package.");
            }
            var folder = Path.Combine(staging, "fse-download");
            Directory.CreateDirectory(folder);
            msix = Path.Combine(folder, msixAsset.Name);
            cer = Path.Combine(folder, cerAsset.Name);
            await releases.DownloadAsync(msixAsset, msix, 512L * 1024 * 1024);
            await releases.DownloadAsync(cerAsset, cer, 64 * 1024);
        }
        await FsePackage.InstallAsync(msix, cer);
    }

    private static bool HasPayloadFse()
    {
        try
        {
            return SetupPayload.Measure(Environment.ProcessPath!).PayloadLength > 0;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static bool IsFseInstalled()
    {
        try
        {
            return FsePackage.IsInstalledAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("setup: could not check the Full Screen Experience package", ex);
            return false;
        }
    }

    /// <summary>A Playdex running from the install folder (not another copy, such as a development build).</summary>
    public static bool IsPlaydexRunning(string executable) =>
        ProcessImage.IsRunningFrom(Path.GetDirectoryName(executable)!, AppUpdate.AppExecutable);

    /// <summary>
    /// Ask a running Playdex to quit (2.0 and later do on --quit; older ones are asked to by the user) and wait for it.
    /// </summary>
    public static async Task ClosePlaydexAsync(string executable, ReleaseVersion installed, Action<string> report)
    {
        if (!IsPlaydexRunning(executable))
        {
            return;
        }
        report("Closing Playdex…");
        if (installed >= _quitsOnRequest)
        {
            Process.Start(new ProcessStartInfo(executable, "--quit") { UseShellExecute = false })?.Dispose();
            for (var i = 0; i < 40 && IsPlaydexRunning(executable); i++)
            {
                await Task.Delay(500);
            }
        }
        if (IsPlaydexRunning(executable))
        {
            report("Quit Playdex to continue: right-click its icon in the notification area, then Quit Playdex.");
            var deadline = DateTime.UtcNow.AddMinutes(10);
            while (IsPlaydexRunning(executable))
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new InvalidOperationException("Playdex is still running. Quit it, then run the setup again.");
                }
                await Task.Delay(500);
            }
        }
        Log.Info("setup: Playdex closed");
    }

    public static bool IsInside(string? path, string directory) =>
        path is not null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Run again from a copy in %TEMP%, so the folder this exe is in can be replaced or removed. The copy deletes itself when done (<see cref="DeleteSelfLater"/>).
    /// </summary>
    public static int RelaunchFromTemp(string[] args, string install)
    {
        var copy = Path.Combine(Path.GetTempPath(), $"Playdex-Setup-{Guid.NewGuid():N}.exe");
        File.Copy(Environment.ProcessPath!, copy);
        var start = new ProcessStartInfo(copy) { UseShellExecute = false };
        foreach (var argument in args.Where(a => !a.StartsWith("--install-dir=", StringComparison.Ordinal)))
        {
            start.ArgumentList.Add(argument);
        }
        start.ArgumentList.Add($"--install-dir={install}");
        start.ArgumentList.Add("--from-temp");
        Process.Start(start)?.Dispose();
        Log.Info($"setup: continuing from {copy}");
        return 0;
    }

    /// <summary>A copy started by <see cref="RelaunchFromTemp"/> removes itself once it has exited.</summary>
    public static void DeleteSelfLater(string[] args)
    {
        if (!args.Contains("--from-temp") || Environment.ProcessPath is not { } self)
        {
            return;
        }
        // A raw command line: cmd doesn't understand the escaped quotes ArgumentList would write.
        var start = new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & del /q \"{self}\"") { UseShellExecute = false, CreateNoWindow = true };
        Process.Start(start)?.Dispose();
    }

    private static int Bundle(string? app, IEnumerable<string> fseFiles, string? output)
    {
        try
        {
            if (app is null || output is null)
            {
                throw new InvalidOperationException("--bundle needs --app=DIR and --out=FILE.");
            }
            SetupPayload.Bundle(Environment.ProcessPath!, Path.GetFullPath(app), fseFiles.Select(Path.GetFullPath).ToList(), Path.GetFullPath(output));
            Log.Info($"setup: bundled {app} into {output} ({new FileInfo(output).Length / (1024 * 1024)} MB)");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Error("setup: bundle failed", ex);
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

    public static string StartMenuShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Playdex.lnk");

    /// <summary>Start menu → Playdex, through Windows Script Host's shortcut object (via PowerShell, no COM interop here).</summary>
    private static void CreateStartMenuShortcut(string executable)
    {
        var shortcut = StartMenuShortcut;
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
    public static partial int MessageBox(nint owner, string text, string caption, uint type);
}
