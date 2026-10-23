using System.Diagnostics;

namespace Playdex.Desktop;

/// <summary>
/// Keeps the desktop app up to date from the download feed (<see cref="ReleaseFeed"/>, <see cref="AppUpdate"/>): checks at startup and once a day,
/// downloads and verifies the new version in the background, and installs it when the user restarts to update. Only
/// installed builds update (a Release build with Playdex.Run.exe next to it in a folder the user can write).
/// </summary>
internal sealed class UpdateFeature(Action changed, Func<bool> gameRunning, Action<string, string> notify) : IDisposable
{
    private static readonly TimeSpan _interval = TimeSpan.FromHours(24);

    private readonly ReleaseFeed _releases = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Lock _gate = new();
    private Task? _check;
    private string? _staged;

    public UpdateView View { get; private set; } = new("idle", "Playdex is up to date.", null);

    /// <summary>The folder Playdex runs from, if this build can update itself.</summary>
    public static string? InstallDirectory
    {
        get
        {
#if DEBUG
            return null;
#else
            var directory = AppContext.BaseDirectory;
            return File.Exists(Path.Combine(directory, AppUpdate.HelperExecutable)) && IsWritable(directory) ? directory : null;
#endif
        }
    }

    public void Start()
    {
        if (AppUpdate.ConsumeFailure(AppUpdate.UpdateRoot) is { } failure)
        {
            View = new UpdateView("failed", failure, null);
            notify("Update not installed", failure);
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(20), _stop.Token);
                while (!_stop.IsCancellationRequested)
                {
                    await CheckAsync(explicitCheck: false);
                    await Task.Delay(_interval, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        });
    }

    public Task CheckAsync(bool explicitCheck)
    {
        lock (_gate)
        {
            if (_check is { IsCompleted: false } || View.Kind == "ready")
            {
                return _check ?? Task.CompletedTask;
            }
            return _check = Task.Run(() => CheckCoreAsync(explicitCheck));
        }
    }

    private async Task CheckCoreAsync(bool explicitCheck)
    {
        if (InstallDirectory is not { } install)
        {
            return;
        }
        Set(new UpdateView("checking", "Checking for updates…", null));
        try
        {
            var running = ReleaseVersion.TryParseTag($"v{DesktopHost.Version}", out var v) ? v : default;
            var release = await _releases.LatestAsync(_stop.Token);
            if (release is null || release.Version <= running)
            {
                Set(new UpdateView("idle", explicitCheck ? $"Playdex {running} is up to date." : "Playdex is up to date.", null));
                return;
            }
            Set(new UpdateView("downloading", $"Downloading Playdex {release.Version}…", release.Version.ToString()));
            _staged = await AppUpdate.StageAsync(_releases, release, install, AppUpdate.UpdateRoot, _stop.Token);
            Set(new UpdateView("ready", $"Playdex {release.Version} is ready. Restart to update.", release.Version.ToString()));
            notify("Playdex update ready", $"Version {release.Version} is downloaded. Open Playdex and restart to update.");
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or TaskCanceledException or UnauthorizedAccessException)
        {
            Log.Error("update: check failed", ex);
            Set(new UpdateView("failed", explicitCheck ? $"Couldn't check for updates: {ex.Message}" : "Couldn't check for updates. Try again later.", null));
        }
    }

    /// <summary>Start the helper that installs the staged update. The caller quits the app straight after.</summary>
    public void Apply()
    {
        if (View.Kind != "ready" || _staged is null)
        {
            throw new InvalidOperationException("No update is ready.");
        }
        if (gameRunning())
        {
            throw new InvalidOperationException("Close the game first: the update restarts Playdex.");
        }
        Set(new UpdateView("applying", $"Installing Playdex {View.Version}…", View.Version));
        using var helper = Process.Start(AppUpdate.PrepareApply(_staged)) ?? throw new InvalidOperationException("The update helper did not start.");
        Log.Info($"update: installing {View.Version}");
    }

    private void Set(UpdateView view)
    {
        View = view;
        changed();
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _releases.Dispose();
    }
}

/// <summary>
/// "Use as Full Screen Experience home": installs the FSE entry's MSIX (it opens Playdex in Big Picture, see FsePackage) from the
/// release Playdex came from. The package is signed with the project's self-signed certificate (the .cer next to it in
/// the release), so Windows must trust that certificate first: one admin prompt imports it into Trusted People. The
/// .cer is only trusted when it is the certificate pinned in FsePackage.SigningCertificateSha256, and the package only
/// installed when it is signed with it (FsePackage.InstallAsync checks both).
/// Developer Mode must be on, because the gamingHome capability is only granted with the developer-mode SCCD.
/// </summary>
internal sealed class FseFeature(Action changed) : IDisposable
{
    private readonly ReleaseFeed _releases = new();

    public FseView View { get; private set; } = new("unavailable", "Checking the Full Screen Experience home…");

    public void Start() => _ = Task.Run(async () =>
    {
        try
        {
            Set(await FsePackage.IsInstalledAsync() ? Installed() : Available());
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Error("fse: state unknown", ex);
            Set(Available());
        }
    });

    private static FseView Installed() =>
        new("installed", "Playdex is ready to be your Full Screen Experience home: choose it in Settings → Gaming → Full screen experience. It opens in Big Picture.");

    private static FseView Available() =>
#if DEBUG
        new("unavailable", "Install the Full Screen Experience package with src/Launcher/Host/tools/package.ps1 in development builds.");
#else
        new("available", "Use Playdex in Big Picture as the Full Screen Experience home (needs Developer Mode and one admin prompt).");
#endif

    public async Task<string> InstallAsync()
    {
        if (!FsePackage.DeveloperModeOn())
        {
            Process.Start(new ProcessStartInfo("ms-settings:developers") { UseShellExecute = true })?.Dispose();
            throw new InvalidOperationException("Turn on Developer Mode first (Settings → System → For developers), then install again.");
        }
        Set(new FseView("installing", "Installing the Full Screen Experience package…"));
        try
        {
            var running = ReleaseVersion.TryParseTag($"v{DesktopHost.Version}", out var v) ? v : throw new InvalidOperationException("Unknown Playdex version.");
            var release = await _releases.ForVersionAsync(running) ?? throw new InvalidOperationException($"Playdex {running} has no release on download.playdex.dev.");
            var msix = release.Asset($"playdex-launcher-{running.Tag}.msix");
            var cer = release.Asset("playdex-launcher.cer");
            if (msix is null || cer is null)
            {
                throw new InvalidOperationException($"Playdex {running} has no signed Full Screen Experience package.");
            }
            var folder = Path.Combine(AppUpdate.UpdateRoot, $"launcher-{running.Tag}");
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
            Directory.CreateDirectory(folder);
            var msixPath = Path.Combine(folder, msix.Name);
            var cerPath = Path.Combine(folder, cer.Name);
            await _releases.DownloadAsync(msix, msixPath, 512L * 1024 * 1024);
            await _releases.DownloadAsync(cer, cerPath, 64 * 1024);
            await FsePackage.InstallAsync(msixPath, cerPath);
            Set(Installed());
            Process.Start(new ProcessStartInfo("ms-settings:gaming") { UseShellExecute = true })?.Dispose();
            return "Installed. In Settings → Gaming → Full screen experience, choose Playdex as the home app.";
        }
        catch (Exception ex)
        {
            Log.Error("fse: install failed", ex);
            Set(new FseView("failed", $"The Full Screen Experience package was not installed: {ex.Message}"));
            throw;
        }
    }

    private void Set(FseView view)
    {
        View = view;
        changed();
    }

    public void Dispose() => _releases.Dispose();
}
