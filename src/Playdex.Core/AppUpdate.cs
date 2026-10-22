using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Playdex.Core;

/// <summary>A stable release version, from a v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt; tag.</summary>
public readonly partial record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public int CompareTo(ReleaseVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor != other.Minor ? Minor.CompareTo(other.Minor) : Patch.CompareTo(other.Patch);

    public static bool TryParseTag(string? tag, out ReleaseVersion version)
    {
        var match = TagPattern().Match(tag ?? "");
        version = match.Success
            ? new ReleaseVersion(Number(match, 1), Number(match, 2), Number(match, 3))
            : default;
        return match.Success;
    }

    private static int Number(Match match, int group) => int.Parse(match.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);

    public static bool operator <(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(ReleaseVersion left, ReleaseVersion right) => left.CompareTo(right) >= 0;

    public string Tag => $"v{this}";

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    [GeneratedRegex("^v(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})\\.(0|[1-9][0-9]{0,8})$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();
}

public sealed record ReleaseAsset(string Name, Uri DownloadUri, long? Size);

public sealed record Release(ReleaseVersion Version, IReadOnlyList<ReleaseAsset> Assets)
{
    public ReleaseAsset? Asset(string name) => Assets.SingleOrDefault(a => a.Name == name);
}

/// <summary>A release in the feed: the shape of GitHub's release JSON, which the feed mirrors.</summary>
internal sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";

    [JsonPropertyName("draft")]
    public bool Draft { get; init; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }

    [JsonPropertyName("assets")]
    public List<ReleaseAssetDto> Assets { get; init; } = [];
}

internal sealed class ReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = "";

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

/// <summary>A staged update, written next to the extracted app and handed to the helper that installs it.</summary>
public sealed record StagedUpdate(
    string Version,
    string ArchiveHash,
    string StagedDirectory,
    string TargetDirectory,
    string UpdateRoot,
    string? HealthSignalPath = null,
    string? FailurePath = null);

/// <summary>How the update helper's <see cref="AppUpdate.ApplyAsync"/> ended.</summary>
public enum UpdateOutcome
{
    /// <summary>The new version started and confirmed it is healthy.</summary>
    Installed,

    /// <summary>Not installed; the previous version is in place (and started again when it had been moved aside).</summary>
    RolledBack,

    /// <summary>Not installed, and the previous version could not be put back: the app folder is not usable.</summary>
    RestoreFailed,
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ReleaseDto))]
[JsonSerializable(typeof(StagedUpdate))]
internal sealed partial class UpdateJson : JsonSerializerContext;

/// <summary>
/// Stable releases from the public download feed (download.playdex.dev, Cloudflare R2): the latest one, or the one for a
/// tag. The release workflow publishes each release's assets and writes windows/releases/&lt;tag&gt;.json and latest.json
/// in GitHub's release JSON shape (tools/downloads/publish.mjs). The GitHub repository is private, so clients never
/// read it.
/// </summary>
/// <remarks>
/// The release JSON is only data from a bucket, so its download URLs are not trusted as they are: every file must come
/// from https on the feed's own host (<see cref="IsFeedUri"/>), and redirects are not followed. A release that lists a
/// file anywhere else is refused as a whole.
/// </remarks>
public sealed class ReleaseFeed(HttpMessageHandler? handler = null) : IDisposable
{
    public static readonly Uri BaseUri = new("https://download.playdex.dev/windows/releases/");
    internal const int MaximumMetadataBytes = 1024 * 1024;

    private readonly HttpClient _http = CreateClient(handler);

    /// <summary>True for an https URL on exactly the feed's host (download.playdex.dev, the default port, no user info).</summary>
    public static bool IsFeedUri(Uri uri) =>
        uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && string.Equals(uri.Host, BaseUri.Host, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && uri.UserInfo.Length == 0;

    private static HttpClient CreateClient(HttpMessageHandler? handler)
    {
        // No redirects: a file comes from the URL that passed IsFeedUri, not from wherever a response points.
        var http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        http.Timeout = TimeSpan.FromMinutes(5);
        http.DefaultRequestHeaders.UserAgent.ParseAdd(Brand.UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    /// <summary>The latest stable release, or null when it is a draft, a prerelease or not a v&lt;x.y.z&gt; tag.</summary>
    public Task<Release?> LatestAsync(CancellationToken cancellationToken = default) =>
        GetAsync(new Uri(BaseUri, "latest.json"), cancellationToken);

    public Task<Release?> ForVersionAsync(ReleaseVersion version, CancellationToken cancellationToken = default) =>
        GetAsync(new Uri(BaseUri, $"{version.Tag}.json"), cancellationToken);

    private async Task<Release?> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
        await using var bounded = new MemoryStream();
        await CopyBoundedAsync(body, bounded, MaximumMetadataBytes, timeout.Token);
        bounded.Position = 0;
        var dto = await JsonSerializer.DeserializeAsync(bounded, UpdateJson.Default.ReleaseDto, timeout.Token);
        return dto is null ? null : ToRelease(dto);
    }

    internal static Release? ToRelease(ReleaseDto dto)
    {
        if (dto.Draft || dto.Prerelease || !ReleaseVersion.TryParseTag(dto.TagName, out var version))
        {
            return null;
        }
        foreach (var asset in dto.Assets)
        {
            if (!Uri.TryCreate(asset.BrowserDownloadUrl, UriKind.Absolute, out var uri) || !IsFeedUri(uri))
            {
                throw new InvalidDataException(
                    $"The release lists {Shorten(asset.Name)} outside https://{BaseUri.Host}; Playdex only downloads release files from there.");
            }
        }
        var assets = dto.Assets
            .GroupBy(a => a.Name, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .Select(g => new ReleaseAsset(g.Key, new Uri(g.Single().BrowserDownloadUrl), g.Single().Size))
            .ToList();
        return new Release(version, assets);
    }

    /// <summary>
    /// Download an asset from the feed's host (<see cref="IsFeedUri"/>) to a new file, refusing anything over
    /// <paramref name="maximumBytes"/>.
    /// </summary>
    public async Task DownloadAsync(ReleaseAsset asset, string destination, long maximumBytes, CancellationToken cancellationToken = default)
    {
        if (!IsFeedUri(asset.DownloadUri))
        {
            throw new InvalidDataException(
                $"{Shorten(asset.Name)} is not on https://{BaseUri.Host}; Playdex only downloads release files from there.");
        }
        if (asset.Size is < 0 || asset.Size > maximumBytes)
        {
            throw new InvalidDataException($"{Shorten(asset.Name)} is not a valid release asset.");
        }
        using var response = await _http.GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await CopyBoundedAsync(source, target, maximumBytes, cancellationToken);
    }

    internal static async Task CopyBoundedAsync(Stream source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > maximumBytes)
            {
                throw new InvalidDataException("The download is larger than allowed.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    /// <summary>A name from the feed, cut short for a message.</summary>
    private static string Shorten(string name) => name.Length <= 80 ? name : $"{name[..80]}…";

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// The desktop app's self-update, a whole folder at a time (Playdex.exe, Playdex.Run.exe, the Windows App SDK, ui/).
/// <list type="number">
/// <item><see cref="StageAsync"/>: download playdex-windows-v&lt;version&gt;.zip and its .sha256 from the release,
/// check the hash, extract it safely into the update folder.</item>
/// <item>The app copies Playdex.Run.exe out of its own folder, starts it with --apply-update and exits.</item>
/// <item><see cref="ApplyAsync"/> (in that helper): wait for the app to exit, move its folder aside, move the new one
/// in, start it and wait for it to confirm a healthy start; otherwise stop it, wait for it to exit, put the old folder
/// back and start it again.</item>
/// </list>
/// </summary>
public static class AppUpdate
{
    public const string AssetPrefix = "playdex-windows-";
    public const string AppExecutable = "Playdex.exe";
    public const string HelperExecutable = "Playdex.Run.exe";
    internal const long MaximumArchiveBytes = 512L * 1024 * 1024;

    /// <summary>Extraction limits, far above the app (v2.1.4: about 140 MB in about 320 files), far below a zip bomb.</summary>
    internal const long MaximumExtractedBytes = 2L * 1024 * 1024 * 1024;

    internal const int MaximumArchiveEntries = 10_000;
    internal static readonly TimeSpan ParentExitTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan HealthTimeout = TimeSpan.FromSeconds(60);

    /// <summary>How long a rollback waits for the stopped new version to exit before it puts the previous one back.</summary>
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Tries at putting the previous version back, half a second apart.</summary>
    internal const int RestoreAttempts = 10;

    /// <summary>
    /// Held by the helper while it replaces the app folder. A Playdex.exe started from that folder meanwhile would lock
    /// it and fail the update, and the Full Screen Experience starts its home app again as soon as Playdex exits.
    /// </summary>
    private const string ApplyingMutexName = @"Local\PlaydexUpdateApplying";

    /// <summary>The helper's lock while it applies an update; held by the calling thread until released or disposed.</summary>
    public static Mutex HoldApplyingLock()
    {
        var applying = new Mutex(initiallyOwned: true, ApplyingMutexName, out var createdNew);
        if (!createdNew)
        {
            // Another helper is still at it: take the lock once it is done.
            try
            {
                applying.WaitOne();
            }
            catch (AbandonedMutexException)
            {
            }
        }
        return applying;
    }

    /// <summary>
    /// If an update is being applied, wait until the helper is done (the new or the restored version is running then).
    /// False when no update was being applied.
    /// </summary>
    public static bool WaitWhileApplying(TimeSpan timeout)
    {
        if (!Mutex.TryOpenExisting(ApplyingMutexName, out var applying))
        {
            return false;
        }
        using (applying)
        {
            try
            {
                if (applying.WaitOne(timeout))
                {
                    applying.ReleaseMutex();
                }
            }
            catch (AbandonedMutexException)
            {
                // The helper ended without releasing it; this thread owns it now.
                applying.ReleaseMutex();
            }
        }
        return true;
    }

    /// <summary>%LOCALAPPDATA%\Playdex\updates.</summary>
    public static string UpdateRoot { get; } = Path.Combine(Paths.DataDirectory, "updates");

    public static string ArchiveName(ReleaseVersion version) => $"{AssetPrefix}{version.Tag}.zip";

    /// <summary>Download, verify and extract <paramref name="release"/>. Returns the manifest's path.</summary>
    public static async Task<string> StageAsync(
        ReleaseFeed releases, Release release, string targetDirectory, string updateRoot, CancellationToken cancellationToken = default)
    {
        var archiveName = ArchiveName(release.Version);
        var archive = release.Asset(archiveName) ?? throw new InvalidDataException($"The release has no {archiveName}.");
        var checksum = release.Asset(archiveName + ".sha256") ?? throw new InvalidDataException($"The release has no {archiveName}.sha256.");

        var staging = Path.Combine(updateRoot, release.Version.Tag);
        if (Directory.Exists(staging))
        {
            Directory.Delete(staging, recursive: true);
        }
        Directory.CreateDirectory(staging);
        try
        {
            var archivePath = Path.Combine(staging, archiveName);
            var checksumPath = archivePath + ".sha256";
            await releases.DownloadAsync(archive, archivePath, MaximumArchiveBytes, cancellationToken);
            await releases.DownloadAsync(checksum, checksumPath, 4096, cancellationToken);
            var expected = ParseChecksum(await File.ReadAllTextAsync(checksumPath, cancellationToken), archiveName);
            var actual = await HashFileAsync(archivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
            {
                throw new InvalidDataException("The downloaded update does not match its checksum.");
            }
            var app = Path.Combine(staging, "app");
            Extract(archivePath, app);
            File.Delete(archivePath);
            var manifest = new StagedUpdate(release.Version.ToString(), actual, app, Path.GetFullPath(targetDirectory), Path.GetFullPath(updateRoot));
            var manifestPath = Path.Combine(staging, "update.json");
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, UpdateJson.Default.StagedUpdate), cancellationToken);
            return manifestPath;
        }
        catch
        {
            TryDeleteDirectory(staging);
            throw;
        }
    }

    /// <summary>"&lt;sha256&gt;  &lt;file&gt;", as `sha256sum` and the release workflow write it.</summary>
    public static string ParseChecksum(string content, string archiveName)
    {
        var match = Regex.Match(content.Trim(), "^([0-9a-fA-F]{64})[ \\t]+\\*?([^\\r\\n]+)$", RegexOptions.CultureInvariant);
        return match.Success && match.Groups[2].Value == archiveName
            ? match.Groups[1].Value.ToLowerInvariant()
            : throw new InvalidDataException("The release checksum file is invalid.");
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    /// <summary>
    /// Extract every entry under <paramref name="destination"/> (<see cref="ExtractSafely"/>, within
    /// <see cref="MaximumExtractedBytes"/> and <see cref="MaximumArchiveEntries"/>); the app must be at the root.
    /// </summary>
    public static void Extract(string archivePath, string destination)
    {
        ExtractSafely(archivePath, destination, MaximumExtractedBytes, MaximumArchiveEntries, "The update");
        foreach (var required in new[] { AppExecutable, HelperExecutable })
        {
            if (!File.Exists(Path.Combine(destination, required)))
            {
                throw new InvalidDataException($"The update has no {required}.");
            }
        }
    }

    /// <summary>
    /// Extract every entry of a zip under <paramref name="destination"/>: no path may leave it (zip slip), and the zip
    /// may hold at most <paramref name="maximumEntries"/> entries and <paramref name="maximumBytes"/> bytes once
    /// extracted. The sizes the zip declares are checked before anything is written, and the bytes actually written
    /// count too, as declared sizes can lie. <paramref name="what"/> starts the error messages ("The update").
    /// </summary>
    internal static void ExtractSafely(string archivePath, string destination, long maximumBytes, int maximumEntries, string what)
    {
        Directory.CreateDirectory(destination);
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var zip = ZipFile.OpenRead(archivePath);
        if (zip.Entries.Count > maximumEntries)
        {
            throw new InvalidDataException($"{what} has more than {maximumEntries} files.");
        }
        long declared = 0;
        foreach (var entry in zip.Entries)
        {
            if (entry.Length < 0 || entry.Length > maximumBytes - declared)
            {
                throw new InvalidDataException($"{what} is larger than allowed once extracted.");
            }
            declared += entry.Length;
        }

        var buffer = new byte[81920];
        long written = 0;
        foreach (var entry in zip.Entries)
        {
            var path = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"{what} contains a path outside its folder.");
            }
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(path);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = entry.Open();
            using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            int read;
            while ((read = source.Read(buffer)) > 0)
            {
                written += read;
                if (written > maximumBytes)
                {
                    throw new InvalidDataException($"{what} is larger than allowed once extracted.");
                }
                target.Write(buffer, 0, read);
            }
        }
    }

    /// <summary>
    /// The app's side of installing: copy the helper out of the app folder (it is about to be replaced), write the
    /// apply manifest with the health and failure paths, and return how to start the helper. The app exits next.
    /// </summary>
    public static ProcessStartInfo PrepareApply(string manifestPath)
    {
        var manifest = Load(manifestPath);
        var attempt = Path.Combine(manifest.UpdateRoot, $"apply-{Guid.NewGuid():N}");
        Directory.CreateDirectory(attempt);
        var helper = Path.Combine(attempt, "Playdex.UpdateHelper.exe");
        File.Copy(Path.Combine(manifest.TargetDirectory, HelperExecutable), helper);
        manifest = manifest with
        {
            HealthSignalPath = Path.Combine(attempt, "healthy.signal"),
            FailurePath = Path.Combine(manifest.UpdateRoot, "last-failure.txt"),
        };
        var applyPath = Path.Combine(attempt, "apply.json");
        File.WriteAllText(applyPath, JsonSerializer.Serialize(manifest, UpdateJson.Default.StagedUpdate));
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = attempt };
        start.ArgumentList.Add("--apply-update");
        start.ArgumentList.Add(applyPath);
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return start;
    }

    /// <summary>The helper's side: install the staged update, or put the previous version back (<see cref="RollBackAsync"/>).</summary>
    public static async Task<UpdateOutcome> ApplyAsync(string applyPath, int parentProcessId, CancellationToken cancellationToken = default)
    {
        StagedUpdate? manifest = null;
        string? backup = null;
        var swapped = false;
        Process? updated = null;
        try
        {
            manifest = Load(applyPath);
            Validate(manifest, applyPath);
            await WaitForExitAsync(parentProcessId, ParentExitTimeout, cancellationToken);
            backup = $"{manifest.TargetDirectory.TrimEnd(Path.DirectorySeparatorChar)}.old-{Guid.NewGuid():N}";
            Directory.Move(manifest.TargetDirectory, backup);
            swapped = true;
            MoveOrCopy(manifest.StagedDirectory, manifest.TargetDirectory);
            Log.Info($"update: installed {manifest.Version} into {manifest.TargetDirectory}");

            var start = new ProcessStartInfo(Path.Combine(manifest.TargetDirectory, AppExecutable)) { UseShellExecute = false };
            start.ArgumentList.Add($"--update-health={manifest.HealthSignalPath}");
            updated = Process.Start(start) ?? throw new InvalidOperationException("The new version did not start.");
            if (!await WaitForHealthAsync(updated, manifest.HealthSignalPath!, HealthTimeout, cancellationToken))
            {
                throw new InvalidOperationException("The new version did not start properly.");
            }
            TryDeleteDirectory(backup);
            TryDelete(manifest.FailurePath!);
            Log.Info($"update: {manifest.Version} is running");
            return UpdateOutcome.Installed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("update: install failed", ex);
            return await RollBackAsync(manifest, swapped ? backup : null, updated, ex);
        }
        finally
        {
            updated?.Dispose();
        }
    }

    /// <summary>
    /// After a failed install: stop the new version if it runs and wait for it to exit (its files must be free), put
    /// <paramref name="backup"/> (the previous version, when it was moved aside) back and start it, and leave the reason
    /// for the app's next start. A restore that does not finish is <see cref="UpdateOutcome.RestoreFailed"/>, never
    /// reported as rolled back.
    /// </summary>
    internal static async Task<UpdateOutcome> RollBackAsync(
        StagedUpdate? manifest, string? backup, Process? updated, Exception error, int restoreAttempts = RestoreAttempts)
    {
        if (updated is not null)
        {
            await StopAsync(updated, StopTimeout);
        }
        var outcome = UpdateOutcome.RolledBack;
        var message = $"The update to {manifest?.Version} could not be installed: {error.Message}";
        if (manifest is not null && backup is not null)
        {
            if (await RestoreAsync(backup, manifest.TargetDirectory, restoreAttempts) is { } restoreError)
            {
                outcome = UpdateOutcome.RestoreFailed;
                message = $"The update to {manifest.Version} could not be installed ({error.Message}), and the previous version "
                    + $"could not be put back ({restoreError.Message}). It is in {backup}; run Playdex Setup to install Playdex again.";
            }
            else
            {
                Log.Info("update: the previous version is back");
                TryStart(Path.Combine(manifest.TargetDirectory, AppExecutable));
            }
        }
        if (manifest?.FailurePath is { } failure)
        {
            TryWrite(failure, message);
        }
        return outcome;
    }

    /// <summary>Kill <paramref name="process"/> (and what it started) and wait up to <paramref name="timeout"/> for it to exit.</summary>
    private static async Task StopAsync(Process process, TimeSpan timeout)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var limit = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            Log.Info($"update: the new version had not exited {timeout.TotalSeconds:0} s after it was stopped");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error("update: could not stop the new version", ex);
        }
    }

    /// <summary>
    /// Remove what was installed and move <paramref name="backup"/> back to <paramref name="target"/>, retrying while
    /// files of a just-stopped process are still being freed. The last error when it could not, else null.
    /// </summary>
    private static async Task<Exception?> RestoreAsync(string backup, string target, int attempts)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
                Directory.Move(backup, target);
                return null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= attempts)
                {
                    Log.Error($"update: could not restore {backup}", ex);
                    return ex;
                }
                Log.Info($"update: could not restore {backup} yet ({ex.Message}); retrying");
                await Task.Delay(500);
            }
        }
    }

    private static void TryStart(string executable)
    {
        try
        {
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Log.Error($"update: could not start {executable}", ex);
        }
    }

    /// <summary>The message the previous update left on failure, once (the file is removed).</summary>
    public static string? ConsumeFailure(string updateRoot)
    {
        var path = Path.Combine(updateRoot, "last-failure.txt");
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            var message = File.ReadAllText(path).Trim();
            File.Delete(path);
            return message.Length > 0 ? message : "The previous update could not be installed.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "The previous update could not be installed.";
        }
    }

    public static StagedUpdate Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 64 * 1024)
        {
            throw new InvalidDataException("The update manifest is missing or too large.");
        }
        return JsonSerializer.Deserialize(File.ReadAllText(path), UpdateJson.Default.StagedUpdate)
            ?? throw new InvalidDataException("The update manifest is empty.");
    }

    /// <summary>Everything the helper touches is in the update folder, except the app folder it replaces.</summary>
    internal static void Validate(StagedUpdate manifest, string applyPath)
    {
        var root = Path.GetFullPath(manifest.UpdateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        bool Inside(string? path) => path is not null && Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        if (!Inside(applyPath) || !Inside(manifest.StagedDirectory) || !Inside(manifest.HealthSignalPath) || !Inside(manifest.FailurePath))
        {
            throw new InvalidDataException("The update manifest points outside the update folder.");
        }
        if (Inside(manifest.TargetDirectory) || !File.Exists(Path.Combine(manifest.TargetDirectory, AppExecutable))
            || !File.Exists(Path.Combine(manifest.StagedDirectory, AppExecutable)))
        {
            throw new InvalidDataException("The update manifest does not name an installed app and a staged one.");
        }
        if (Path.GetFullPath(Environment.ProcessPath ?? "").StartsWith(
                Path.GetFullPath(manifest.TargetDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update helper must run from outside the app folder.");
        }
    }

    private static async Task WaitForExitAsync(int processId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limit.CancelAfter(timeout);
            await process.WaitForExitAsync(limit.Token);
        }
        catch (ArgumentException)
        {
            // Already gone.
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("Playdex did not exit for the update.");
        }
    }

    private static async Task<bool> WaitForHealthAsync(Process process, string healthSignal, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (File.Exists(healthSignal))
            {
                return true;
            }
            if (process.HasExited)
            {
                return false;
            }
            await Task.Delay(200, cancellationToken);
        }
        return File.Exists(healthSignal);
    }

    /// <summary>A move on one volume; a copy and delete when the update folder is on another one.</summary>
    private static void MoveOrCopy(string source, string destination)
    {
        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException) when (!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            TryDeleteDirectory(source);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info($"update: could not delete {path} yet: {ex.Message}");
        }
    }

    private static void TryWrite(string path, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
