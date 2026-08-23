using System.Reflection;

namespace Wrapper;

internal readonly record struct StableVersion(int Major, int Minor, int Patch) : IComparable<StableVersion>
{
    private static readonly Regex TagPattern = new(
        "^v(?<major>0|[1-9][0-9]*)\\.(?<minor>0|[1-9][0-9]*)\\.(?<patch>0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    public int CompareTo(StableVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public static bool TryParseTag(string? tag, out StableVersion version)
    {
        var match = TagPattern.Match(tag ?? "");
        if (!match.Success ||
            !int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
        {
            version = default;
            return false;
        }

        version = new StableVersion(major, minor, patch);
        return true;
    }

    public static StableVersion Running(Assembly? assembly = null)
    {
        var value = (assembly ?? typeof(StableVersion).Assembly).GetName().Version;
        return value is null
            ? default
            : new StableVersion(Math.Max(0, value.Major), Math.Max(0, value.Minor), Math.Max(0, value.Build));
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

internal sealed record UpdateAsset(string Name, Uri DownloadUri, long? Size);

internal sealed record UpdateRelease(
    StableVersion Version,
    string Tag,
    UpdateAsset Archive,
    UpdateAsset Checksum);

internal interface IUpdateSource : IDisposable
{
    Task<UpdateRelease?> GetLatestAsync(StableVersion runningVersion, CancellationToken cancellationToken);
    Task DownloadAsync(UpdateAsset asset, string destinationPath, long maximumBytes, CancellationToken cancellationToken);
}

internal sealed class GitHubReleaseClient : IUpdateSource
{
    internal const int MaximumReleaseResponseBytes = 1024 * 1024;
    internal const long MaximumArchiveBytes = 256L * 1024 * 1024;
    internal const long MaximumChecksumBytes = 4096;
    internal static readonly TimeSpan MaximumAssetDownloadDuration = TimeSpan.FromMinutes(2);
    private static readonly Uri LatestReleaseUri = new(AppBrand.GitHubLatestReleaseUrl);
    private readonly HttpClient client;
    private readonly TimeSpan metadataTimeout;

    public GitHubReleaseClient(HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        metadataTimeout = timeout ?? TimeSpan.FromSeconds(15);
        client.Timeout = Timeout.InfiniteTimeSpan;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppBrand.Name}-Wrapper-Updater/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<UpdateRelease?> GetLatestAsync(
        StableVersion runningVersion,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(metadataTimeout);
        var operationToken = timeoutSource.Token;
        using var response = await client.GetAsync(
            LatestReleaseUri,
            HttpCompletionOption.ResponseHeadersRead,
            operationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumReleaseResponseBytes)
        {
            throw new InvalidDataException("The GitHub release response is too large.");
        }

        await using var responseStream = await response.Content.ReadAsStreamAsync(operationToken);
        await using var bounded = new MemoryStream();
        await CopyBoundedAsync(responseStream, bounded, MaximumReleaseResponseBytes, operationToken);
        bounded.Position = 0;
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(bounded, JsonOptions.Create(), operationToken)
            ?? throw new InvalidDataException("GitHub returned an empty release response.");
        return SelectRelease(release, runningVersion);
    }

    public async Task DownloadAsync(
        UpdateAsset asset,
        string destinationPath,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        if (asset.DownloadUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("Update assets must use HTTPS.");
        }
        if (asset.Size is < 0 || asset.Size > maximumBytes)
        {
            throw new InvalidDataException($"Update asset '{asset.Name}' exceeds the allowed size.");
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(MaximumAssetDownloadDuration);
        var operationToken = timeoutSource.Token;
        using var response = await client.GetAsync(
            asset.DownloadUri,
            HttpCompletionOption.ResponseHeadersRead,
            operationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException($"Update asset '{asset.Name}' exceeds the allowed size.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(operationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await CopyBoundedAsync(source, destination, maximumBytes, operationToken);
        await destination.FlushAsync(operationToken);
    }

    internal static UpdateRelease? SelectRelease(GitHubReleaseDto release, StableVersion runningVersion)
    {
        if (release.Draft || release.Prerelease || !StableVersion.TryParseTag(release.TagName, out var version))
        {
            return null;
        }
        if (version.CompareTo(runningVersion) <= 0)
        {
            return null;
        }

        var archiveName = $"{AppBrand.ReleaseAssetPrefix}{release.TagName}.zip";
        var checksumName = $"{archiveName}.sha256";
        var archive = SelectAsset(release.Assets, archiveName);
        var checksum = SelectAsset(release.Assets, checksumName);
        return archive is null || checksum is null
            ? null
            : new UpdateRelease(version, release.TagName, archive, checksum);
    }

    private static UpdateAsset? SelectAsset(IEnumerable<GitHubAssetDto>? assets, string name)
    {
        var matches = (assets ?? []).Where(asset => string.Equals(asset.Name, name, StringComparison.Ordinal)).ToList();
        if (matches.Count != 1 ||
            !Uri.TryCreate(matches[0].BrowserDownloadUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new UpdateAsset(name, uri, matches[0].Size);
    }

    internal static async Task CopyBoundedAsync(
        Stream source,
        Stream destination,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0) return;
            total = checked(total + read);
            if (total > maximumBytes)
            {
                throw new InvalidDataException("Downloaded content exceeds the allowed size.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public void Dispose() => client.Dispose();
}

internal sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = "";
    [JsonPropertyName("draft")]
    public bool Draft { get; init; }
    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; init; }
    [JsonPropertyName("assets")]
    public List<GitHubAssetDto> Assets { get; init; } = [];
}

internal sealed class GitHubAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; init; } = "";
    [JsonPropertyName("size")]
    public long Size { get; init; }
}

internal sealed record StagedUpdateManifest(
    string Version,
    string ArchiveHash,
    string ExecutableHash,
    string StagedExecutablePath,
    string TargetExecutablePath,
    string UpdateRoot,
    DateTimeOffset CreatedAt,
    string? HealthSignalPath = null,
    string? FailurePath = null);

internal sealed class UpdatePackageStager(string updateRoot)
{
    internal const string ExpectedExecutableName = "Wrapper.exe";
    private static readonly TimeSpan MaximumStagingAge = TimeSpan.FromDays(14);
    private readonly string root = Path.GetFullPath(updateRoot);

    public async Task<(StagedUpdateManifest Manifest, string ManifestPath)> StageAsync(
        IUpdateSource source,
        UpdateRelease release,
        string targetExecutablePath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(root);
        var stagingDirectory = Path.Combine(root, release.Tag);
        if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        Directory.CreateDirectory(stagingDirectory);
        var archivePath = Path.Combine(stagingDirectory, release.Archive.Name);
        var checksumPath = Path.Combine(stagingDirectory, release.Checksum.Name);
        try
        {
            await source.DownloadAsync(release.Archive, archivePath, GitHubReleaseClient.MaximumArchiveBytes, cancellationToken);
            await source.DownloadAsync(release.Checksum, checksumPath, GitHubReleaseClient.MaximumChecksumBytes, cancellationToken);
            var expectedHash = ParseChecksum(await File.ReadAllTextAsync(checksumPath, cancellationToken), release.Archive.Name);
            var archiveHash = await HashFileAsync(archivePath, cancellationToken);
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expectedHash), Convert.FromHexString(archiveHash)))
            {
                throw new InvalidDataException("The downloaded update archive checksum does not match the release checksum.");
            }

            var extractedPath = ExtractExpectedExecutable(archivePath, stagingDirectory);
            var executableHash = await HashFileAsync(extractedPath, cancellationToken);
            var manifest = new StagedUpdateManifest(
                release.Version.ToString(),
                archiveHash,
                executableHash,
                extractedPath,
                Path.GetFullPath(targetExecutablePath),
                root,
                DateTimeOffset.UtcNow);
            var manifestPath = Path.Combine(stagingDirectory, "update.json");
            await WriteManifestAsync(manifestPath, manifest, cancellationToken);
            CleanupExcept(stagingDirectory);
            return (manifest, manifestPath);
        }
        catch
        {
            try { Directory.Delete(stagingDirectory, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    public static async Task<bool> VerifyAsync(StagedUpdateManifest manifest, CancellationToken cancellationToken)
    {
        if (!ValidateManifestPaths(manifest) || !File.Exists(manifest.StagedExecutablePath)) return false;
        var actual = await HashFileAsync(manifest.StagedExecutablePath, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(manifest.ExecutableHash),
            Convert.FromHexString(actual));
    }

    public static string ParseChecksum(string content, string archiveName)
    {
        var match = Regex.Match(
            content.Trim(),
            "^(?<hash>[0-9a-fA-F]{64})[ \\t]+\\*?(?<file>[^\\r\\n]+)$",
            RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);
        if (!match.Success || !string.Equals(match.Groups["file"].Value, archiveName, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The release checksum file is invalid.");
        }
        return match.Groups["hash"].Value.ToLowerInvariant();
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(hash);
    }

    internal static string ExtractExpectedExecutable(string archivePath, string stagingDirectory)
    {
        var extractionRoot = Path.Combine(stagingDirectory, "extracted");
        Directory.CreateDirectory(extractionRoot);
        var rootWithSeparator = Path.GetFullPath(extractionRoot) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        ZipArchiveEntry? executable = null;
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
            if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The update archive contains an unsafe path.");
            }
            if (string.Equals(Path.GetFileName(entry.FullName), ExpectedExecutableName, StringComparison.OrdinalIgnoreCase))
            {
                if (executable is not null) throw new InvalidDataException("The update archive contains multiple wrapper executables.");
                executable = entry;
            }
        }

        if (executable is null || string.IsNullOrEmpty(executable.Name))
        {
            throw new InvalidDataException("The update archive does not contain Wrapper.exe.");
        }
        var output = Path.Combine(extractionRoot, ExpectedExecutableName);
        executable.ExtractToFile(output);
        return output;
    }

    internal static bool ValidateManifestPaths(StagedUpdateManifest manifest)
    {
        try
        {
            var rootPath = Path.GetFullPath(manifest.UpdateRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var stagedPath = Path.GetFullPath(manifest.StagedExecutablePath);
            var targetPath = Path.GetFullPath(manifest.TargetExecutablePath);
            return stagedPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetFileName(stagedPath), ExpectedExecutableName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Path.GetExtension(targetPath), ".exe", StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(manifest.ExecutableHash, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant) &&
                StableVersion.TryParseTag($"v{manifest.Version}", out _);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    internal static async Task WriteManifestAsync(string path, StagedUpdateManifest manifest, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions.Create(), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporary, path, overwrite: true);
    }

    internal void CleanupExcept(string keepDirectory)
    {
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(keepDirectory), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(directory) > MaximumStagingAge ||
                    Path.GetFileName(directory).StartsWith('v'))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

internal enum UpdateStateKind
{
    Idle,
    Checking,
    Downloading,
    Ready,
    Applying,
    Failed
}

internal sealed record UpdateState(UpdateStateKind Kind, string Message, StableVersion? Version = null)
{
    public static UpdateState Idle { get; } = new(UpdateStateKind.Idle, "Check for updates");
}

internal sealed partial class UpdateCoordinator : IDisposable
{
    private readonly IUpdateSource source;
    private readonly UpdatePackageStager stager;
    private readonly StableVersion runningVersion;
    private readonly string targetExecutablePath;
    private readonly ILogger<UpdateCoordinator> logger;
    private readonly object sync = new();
    private Task? checkTask;
    private string? manifestPath;
    private StagedUpdateManifest? manifest;

    public event EventHandler<UpdateState>? StateChanged;
    public UpdateState State { get; private set; } = UpdateState.Idle;
    public bool IsReady => State.Kind == UpdateStateKind.Ready && manifest is not null && manifestPath is not null;

    public UpdateCoordinator(
        IUpdateSource source,
        UpdatePackageStager stager,
        StableVersion runningVersion,
        string targetExecutablePath,
        ILogger<UpdateCoordinator> logger)
    {
        this.source = source;
        this.stager = stager;
        this.runningVersion = runningVersion;
        this.targetExecutablePath = Path.GetFullPath(targetExecutablePath);
        this.logger = logger;
    }

    public Task CheckAsync(bool explicitCheck, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            if (checkTask is { IsCompleted: false }) return checkTask;
            checkTask = CheckCoreAsync(explicitCheck, cancellationToken);
            return checkTask;
        }
    }

    private async Task CheckCoreAsync(bool explicitCheck, CancellationToken cancellationToken)
    {
        SetState(new UpdateState(UpdateStateKind.Checking, "Checking for updates..."));
        try
        {
            var release = await source.GetLatestAsync(runningVersion, cancellationToken);
            if (release is null)
            {
                SetState(new UpdateState(UpdateStateKind.Idle, explicitCheck ? $"{AppBrand.Name} is up to date" : "Check for updates"));
                return;
            }

            SetState(new UpdateState(UpdateStateKind.Downloading, $"Downloading {release.Version}...", release.Version));
            var staged = await stager.StageAsync(source, release, targetExecutablePath, cancellationToken);
            manifest = staged.Manifest;
            manifestPath = staged.ManifestPath;
            SetState(new UpdateState(UpdateStateKind.Ready, $"Restart to update to {release.Version}", release.Version));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetState(new UpdateState(UpdateStateKind.Idle, "Check for updates"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or TaskCanceledException)
        {
            LogUpdateCheckFailed(logger, ex);
            SetState(new UpdateState(UpdateStateKind.Failed, explicitCheck ? "Update check failed" : "Check for updates"));
        }
    }

    public async Task<ProcessStartInfo> PrepareApplyAsync(CancellationToken cancellationToken = default)
    {
        if (manifest is null || manifestPath is null || !await UpdatePackageStager.VerifyAsync(manifest, cancellationToken))
        {
            throw new InvalidDataException("The staged update is missing or no longer valid.");
        }
        if (!string.Equals(Path.GetFullPath(Environment.ProcessPath ?? ""), targetExecutablePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The running executable is not the update target.");
        }

        var attemptDirectory = Path.Combine(manifest.UpdateRoot, $"apply-{Guid.NewGuid():N}");
        Directory.CreateDirectory(attemptDirectory);
        var helperPath = Path.Combine(attemptDirectory, $"{AppBrand.Name}.UpdateHelper.exe");
        File.Copy(targetExecutablePath, helperPath);
        var healthPath = Path.Combine(attemptDirectory, "healthy.signal");
        var failurePath = Path.Combine(manifest.UpdateRoot, "last-failure.txt");
        manifest = manifest with { HealthSignalPath = healthPath, FailurePath = failurePath };
        manifestPath = Path.Combine(attemptDirectory, "apply.json");
        await UpdatePackageStager.WriteManifestAsync(manifestPath, manifest, cancellationToken);
        SetState(new UpdateState(UpdateStateKind.Applying, $"Installing {manifest.Version}...", State.Version));
        return UpdateCommandLine.CreateHelperStartInfo(helperPath, manifestPath, Environment.ProcessId);
    }

    private void SetState(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose() => source.Dispose();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Wrapper update check or staging failed")]
    private static partial void LogUpdateCheckFailed(ILogger logger, Exception exception);
}
