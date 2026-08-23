using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Wrapper;

namespace Wrapper.Tests;

public sealed class AutoUpdateTests
{
    [Theory]
    [InlineData("v0.0.0", 0, 0, 0)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("v2147483647.0.9", 2147483647, 0, 9)]
    public void TestStableVersionTags(string tag, int major, int minor, int patch)
    {
        Assert.True(StableVersion.TryParseTag(tag, out var version));
        Assert.Equal(new StableVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3-beta")]
    [InlineData("v01.2.3")]
    [InlineData("v1.2.3.4")]
    public void TestInvalidStableVersionTags(string tag) =>
        Assert.False(StableVersion.TryParseTag(tag, out _));

    [Fact]
    public void TestVersionOrderingAndRunningVersion()
    {
        Assert.True(new StableVersion(2, 0, 0).CompareTo(new StableVersion(1, 99, 99)) > 0);
        Assert.True(new StableVersion(1, 3, 0).CompareTo(new StableVersion(1, 2, 99)) > 0);
        Assert.True(new StableVersion(1, 2, 4).CompareTo(new StableVersion(1, 2, 3)) > 0);
        Assert.Equal(0, new StableVersion(1, 2, 3).CompareTo(new StableVersion(1, 2, 3)));
        Assert.True(StableVersion.Running().Major >= 0);
    }

    [Fact]
    public void TestReleaseSelectionRequiresNewStableExactAssets()
    {
        var dto = ReleaseDto("v2.0.0");
        var selected = GitHubReleaseClient.SelectRelease(dto, new StableVersion(1, 9, 9));
        Assert.NotNull(selected);
        Assert.Equal($"{AppBrand.ReleaseAssetPrefix}v2.0.0.zip", selected.Archive.Name);

        Assert.Null(GitHubReleaseClient.SelectRelease(dto, new StableVersion(2, 0, 0)));
        Assert.Null(GitHubReleaseClient.SelectRelease(dto, new StableVersion(3, 0, 0)));
        Assert.Null(GitHubReleaseClient.SelectRelease(ReleaseDto("v2.0.0", draft: true), default));
        Assert.Null(GitHubReleaseClient.SelectRelease(ReleaseDto("v2.0.0", prerelease: true), default));
        Assert.Null(GitHubReleaseClient.SelectRelease(ReleaseDto("v2.0.0-beta"), default));
        dto.Assets.RemoveAt(1);
        Assert.Null(GitHubReleaseClient.SelectRelease(dto, default));
    }

    [Fact]
    public async Task TestGitHubClientUsesRequiredHeadersAndParsesRelease()
    {
        var handler = new UpdateHttpHandler(request =>
        {
            Assert.Contains($"{AppBrand.Name}-Wrapper-Updater", request.Headers.UserAgent.ToString());
            Assert.Contains("application/vnd.github+json", request.Headers.Accept.ToString());
            Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
            return JsonResponse(ReleaseJson("v3.2.1"));
        });
        using var client = new GitHubReleaseClient(handler);

        var release = await client.GetLatestAsync(new StableVersion(3, 2, 0), CancellationToken.None);

        Assert.Equal(new StableVersion(3, 2, 1), release?.Version);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task TestGitHubClientRejectsHttpAndOversizedOrMalformedResponses()
    {
        using (var failed = new GitHubReleaseClient(new UpdateHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests))))
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => failed.GetLatestAsync(default, CancellationToken.None));
        }
        using (var malformed = new GitHubReleaseClient(new UpdateHttpHandler(_ => JsonResponse("{"))))
        {
            await Assert.ThrowsAsync<JsonException>(() => malformed.GetLatestAsync(default, CancellationToken.None));
        }
        using (var oversized = new GitHubReleaseClient(new UpdateHttpHandler(_ =>
        {
            var response = JsonResponse("{}");
            response.Content.Headers.ContentLength = GitHubReleaseClient.MaximumReleaseResponseBytes + 1;
            return response;
        })))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => oversized.GetLatestAsync(default, CancellationToken.None));
        }
    }

    [Fact]
    public async Task TestGitHubClientTimeoutIsBounded()
    {
        using var client = new GitHubReleaseClient(
            new AsyncUpdateHttpHandler(async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return JsonResponse("{}");
            }),
            TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLatestAsync(default, CancellationToken.None));
    }

    [Fact]
    public async Task TestBoundedCopyRejectsExcessContent()
    {
        await using var source = new MemoryStream(new byte[17]);
        await using var destination = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            GitHubReleaseClient.CopyBoundedAsync(source, destination, 16, CancellationToken.None));
    }

    [Fact]
    public async Task TestVerifiedPackageStagesAndManifestReverifies()
    {
        var directory = TestPaths.NewTempDir();
        var archive = CreateZip(("Wrapper.exe", Encoding.UTF8.GetBytes("new wrapper")));
        var source = MemoryUpdateSource.Create("v2.0.0", archive);
        var stager = new UpdatePackageStager(Path.Combine(directory, "updates"));

        var staged = await stager.StageAsync(source, source.Release!, Path.Combine(directory, "Wrapper.exe"), CancellationToken.None);

        Assert.True(File.Exists(staged.ManifestPath));
        Assert.True(File.Exists(staged.Manifest.StagedExecutablePath));
        Assert.True(await UpdatePackageStager.VerifyAsync(staged.Manifest, CancellationToken.None));
        Assert.Equal("new wrapper", await File.ReadAllTextAsync(staged.Manifest.StagedExecutablePath));
    }

    [Fact]
    public async Task TestStagingRejectsChecksumMismatchAndCleansFiles()
    {
        var directory = TestPaths.NewTempDir();
        var source = MemoryUpdateSource.Create("v2.0.0", CreateZip(("Wrapper.exe", [1, 2, 3])));
        source.Content[source.Release!.Checksum.Name] = Encoding.UTF8.GetBytes($"{new string('0', 64)}  {source.Release.Archive.Name}");
        var stager = new UpdatePackageStager(Path.Combine(directory, "updates"));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            stager.StageAsync(source, source.Release, Path.Combine(directory, "Wrapper.exe"), CancellationToken.None));

        Assert.False(Directory.Exists(Path.Combine(directory, "updates", "v2.0.0")));
    }

    [Theory]
    [InlineData("../Wrapper.exe")]
    [InlineData("missing.exe")]
    public async Task TestStagingRejectsUnsafeOrMissingExecutable(string entryName)
    {
        var directory = TestPaths.NewTempDir();
        var source = MemoryUpdateSource.Create("v2.0.0", CreateZip((entryName, [1, 2, 3])));
        var stager = new UpdatePackageStager(Path.Combine(directory, "updates"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            stager.StageAsync(source, source.Release!, Path.Combine(directory, "Wrapper.exe"), CancellationToken.None));
    }

    [Fact]
    public async Task TestStagingRejectsOversizedTransportAndTampering()
    {
        var directory = TestPaths.NewTempDir();
        var source = MemoryUpdateSource.Create("v2.0.0", CreateZip(("Wrapper.exe", [1, 2, 3])));
        source.ReportedArchiveSize = GitHubReleaseClient.MaximumArchiveBytes + 1;
        var stager = new UpdatePackageStager(Path.Combine(directory, "updates"));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            stager.StageAsync(source, source.Release!, Path.Combine(directory, "Wrapper.exe"), CancellationToken.None));

        source.ReportedArchiveSize = null;
        var staged = await stager.StageAsync(source, source.Release!, Path.Combine(directory, "Wrapper.exe"), CancellationToken.None);
        await File.AppendAllTextAsync(staged.Manifest.StagedExecutablePath, "tampered");
        Assert.False(await UpdatePackageStager.VerifyAsync(staged.Manifest, CancellationToken.None));
    }

    [Fact]
    public async Task TestNewStageCleansSupersededRelease()
    {
        var directory = TestPaths.NewTempDir();
        var root = Path.Combine(directory, "updates");
        var old = Path.Combine(root, "v1.0.0");
        Directory.CreateDirectory(old);
        await File.WriteAllTextAsync(Path.Combine(old, "old"), "old");
        var source = MemoryUpdateSource.Create("v2.0.0", CreateZip(("Wrapper.exe", [1])));

        await new UpdatePackageStager(root).StageAsync(source, source.Release!, Path.Combine(directory, "Wrapper.exe"), CancellationToken.None);

        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(Path.Combine(root, "v2.0.0")));
    }

    [Theory]
    [InlineData("not a checksum")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000  other.zip")]
    public void TestStrictChecksumParsing(string checksum) =>
        Assert.Throws<InvalidDataException>(() => UpdatePackageStager.ParseChecksum(checksum, "archive.zip"));

    [Fact]
    public async Task TestCoordinatorCoalescesChecksAndReachesReady()
    {
        var directory = TestPaths.NewTempDir();
        var source = MemoryUpdateSource.Create("v2.0.0", CreateZip(("Wrapper.exe", [1, 2, 3])));
        source.DiscoveryGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new UpdateCoordinator(
            source,
            new UpdatePackageStager(Path.Combine(directory, "updates")),
            new StableVersion(1, 0, 0),
            Path.Combine(directory, "Wrapper.exe"),
            NullLogger<UpdateCoordinator>.Instance);

        var first = coordinator.CheckAsync(explicitCheck: false);
        var second = coordinator.CheckAsync(explicitCheck: true);
        Assert.Same(first, second);
        source.DiscoveryGate.SetResult();
        await first;

        Assert.Equal(1, source.DiscoveryCount);
        Assert.Equal(UpdateStateKind.Ready, coordinator.State.Kind);
    }

    [Fact]
    public async Task TestCoordinatorFailureAndCurrentPresentation()
    {
        var source = new MemoryUpdateSource { DiscoveryException = new HttpRequestException("offline") };
        using var failed = new UpdateCoordinator(
            source,
            new UpdatePackageStager(TestPaths.NewTempDir()),
            default,
            TestProcess.ExecutablePath,
            NullLogger<UpdateCoordinator>.Instance);
        await failed.CheckAsync(explicitCheck: true);
        Assert.Equal(UpdateStateKind.Failed, failed.State.Kind);

        using var current = new UpdateCoordinator(
            new MemoryUpdateSource(),
            new UpdatePackageStager(TestPaths.NewTempDir()),
            default,
            TestProcess.ExecutablePath,
            NullLogger<UpdateCoordinator>.Instance);
        await current.CheckAsync(explicitCheck: true);
        Assert.Equal($"{AppBrand.Name} is up to date", current.State.Message);
    }

    [Fact]
    public void TestInternalUpdateCommandParsingAndQuoting()
    {
        var path = Path.Combine(TestPaths.NewTempDir(), "apply manifest.json");
        Assert.True(UpdateCommandLine.TryGetApply(["--apply-update", path, "42"], out var parsed, out var pid));
        Assert.Equal(Path.GetFullPath(path), parsed);
        Assert.Equal(42, pid);
        Assert.False(UpdateCommandLine.TryGetApply(["--apply-update", "relative", "0"], out _, out _));

        var helper = UpdateCommandLine.CreateHelperStartInfo(TestProcess.ExecutablePath, path, 42);
        Assert.False(helper.UseShellExecute);
        Assert.Equal(["--apply-update", Path.GetFullPath(path), "42"], helper.ArgumentList);
        Assert.False(UpdateCommandLine.TryGetHealthSignal(["--update-health", path], out _));
        var allowedHealth = Path.Combine(AppBrand.DefaultUpdateDirectory, "test", "healthy.signal");
        Assert.True(UpdateCommandLine.TryGetHealthSignal(["--update-health", allowedHealth], out var parsedHealth));
        Assert.Equal(Path.GetFullPath(allowedHealth), parsedHealth);
    }

    [Fact]
    public async Task TestHealthConfirmationDetectsSignalAndEarlyExit()
    {
        var signal = Path.Combine(TestPaths.NewTempDir(), "healthy.signal");
        using (var running = TestProcess.Start())
        {
            var wait = UpdateApplier.WaitForHealthAsync(running, signal, TimeSpan.FromSeconds(2), CancellationToken.None);
            UpdateHealthSignal.Confirm(signal);
            Assert.True(await wait);
            if (!running.HasExited) running.Kill(entireProcessTree: true);
        }

        using var exited = Process.Start(new ProcessStartInfo(TestProcess.ExecutablePath, "/d /c exit 0")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Test process did not start.");
        Assert.False(await UpdateApplier.WaitForHealthAsync(
            exited,
            Path.Combine(TestPaths.NewTempDir(), "missing.signal"),
            TimeSpan.FromSeconds(2),
            CancellationToken.None));
    }

    [Fact]
    public async Task TestLockedTargetRejectsReplacementWithoutLosingOriginal()
    {
        var directory = TestPaths.NewTempDir();
        var updateRoot = Path.Combine(directory, "updates");
        Directory.CreateDirectory(updateRoot);
        var target = Path.Combine(directory, "Wrapper.exe");
        var staged = Path.Combine(updateRoot, "Wrapper.exe");
        await File.WriteAllTextAsync(target, "old");
        await File.WriteAllTextAsync(staged, "new");
        var hash = await UpdatePackageStager.HashFileAsync(staged, CancellationToken.None);
        var manifest = new StagedUpdateManifest("2.0.0", hash, hash, staged, target, updateRoot, DateTimeOffset.UtcNow);

        await using (File.Open(target, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Throws<IOException>(() => UpdateApplier.ReplaceTarget(manifest));
        }
        Assert.Equal("old", await File.ReadAllTextAsync(target));
    }

    [Fact]
    public async Task TestReplacementAndRollbackPreserveOldExecutable()
    {
        var directory = TestPaths.NewTempDir();
        var updateRoot = Path.Combine(directory, "updates");
        Directory.CreateDirectory(updateRoot);
        var target = Path.Combine(directory, "Wrapper.exe");
        var staged = Path.Combine(updateRoot, "Wrapper.exe");
        await File.WriteAllTextAsync(target, "old");
        await File.WriteAllTextAsync(staged, "new");
        var hash = await UpdatePackageStager.HashFileAsync(staged, CancellationToken.None);
        var manifest = new StagedUpdateManifest("2.0.0", hash, hash, staged, target, updateRoot, DateTimeOffset.UtcNow);

        var backup = UpdateApplier.ReplaceTarget(manifest);
        Assert.Equal("new", await File.ReadAllTextAsync(target));
        Assert.Equal("old", await File.ReadAllTextAsync(backup));

        UpdateApplier.TryRestore(target, backup);
        Assert.Equal("old", await File.ReadAllTextAsync(target));
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public async Task TestApplyManifestRejectsOutsidePathsAndTamperedStage()
    {
        var directory = TestPaths.NewTempDir();
        var root = Path.Combine(directory, "updates");
        Directory.CreateDirectory(root);
        var staged = Path.Combine(root, "Wrapper.exe");
        await File.WriteAllTextAsync(staged, "new");
        var hash = await UpdatePackageStager.HashFileAsync(staged, CancellationToken.None);
        var manifest = new StagedUpdateManifest(
            "2.0.0", hash, hash, staged, Path.Combine(directory, "Target.exe"), root, DateTimeOffset.UtcNow,
            Path.Combine(root, "health"), Path.Combine(directory, "outside.txt"));

        Assert.Throws<InvalidDataException>(() => UpdateApplier.ValidateApplyManifest(manifest, Path.Combine(root, "apply.json")));
        await File.AppendAllTextAsync(staged, "tampered");
        Assert.False(await UpdatePackageStager.VerifyAsync(manifest, CancellationToken.None));
    }

    [Fact]
    public void TestFailureStateIsConsumedOnce()
    {
        var root = TestPaths.NewTempDir();
        File.WriteAllText(Path.Combine(root, "last-failure.txt"), "rolled back");
        Assert.Equal("rolled back", UpdateFailureStore.Consume(root));
        Assert.Null(UpdateFailureStore.Consume(root));
    }

    private static GitHubReleaseDto ReleaseDto(string tag, bool draft = false, bool prerelease = false)
    {
        var archive = $"{AppBrand.ReleaseAssetPrefix}{tag}.zip";
        return new GitHubReleaseDto
        {
            TagName = tag,
            Draft = draft,
            Prerelease = prerelease,
            Assets =
            [
                new GitHubAssetDto { Name = archive, BrowserDownloadUrl = $"https://example.test/{archive}", Size = 10 },
                new GitHubAssetDto { Name = archive + ".sha256", BrowserDownloadUrl = $"https://example.test/{archive}.sha256", Size = 100 }
            ]
        };
    }

    private static string ReleaseJson(string tag)
    {
        var archive = $"{AppBrand.ReleaseAssetPrefix}{tag}.zip";
        return $$"""
            {"tag_name":"{{tag}}","draft":false,"prerelease":false,"assets":[
              {"name":"{{archive}}","browser_download_url":"https://example.test/{{archive}}","size":10},
              {"name":"{{archive}}.sha256","browser_download_url":"https://example.test/{{archive}}.sha256","size":100}
            ]}
            """;
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static byte[] CreateZip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name);
                using var output = entry.Open();
                output.Write(item.Content);
            }
        }
        return stream.ToArray();
    }
}

internal sealed class UpdateHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        return Task.FromResult(response(request));
    }
}

internal sealed class AsyncUpdateHttpHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        response(request, cancellationToken);
}

internal sealed class MemoryUpdateSource : IUpdateSource
{
    public UpdateRelease? Release { get; init; }
    public Dictionary<string, byte[]> Content { get; } = new(StringComparer.Ordinal);
    public long? ReportedArchiveSize { get; set; }
    public Exception? DiscoveryException { get; init; }
    public TaskCompletionSource? DiscoveryGate { get; set; }
    public int DiscoveryCount { get; private set; }

    public static MemoryUpdateSource Create(string tag, byte[] archive)
    {
        if (!StableVersion.TryParseTag(tag, out var version))
        {
            throw new ArgumentException("A stable release tag is required.", nameof(tag));
        }
        var archiveName = $"{AppBrand.ReleaseAssetPrefix}{tag}.zip";
        var checksumName = archiveName + ".sha256";
        var hash = Convert.ToHexStringLower(SHA256.HashData(archive));
        var source = new MemoryUpdateSource
        {
            Release = new UpdateRelease(
                version,
                tag,
                new UpdateAsset(archiveName, new Uri($"https://example.test/{archiveName}"), archive.Length),
                new UpdateAsset(checksumName, new Uri($"https://example.test/{checksumName}"), 100))
        };
        source.Content[archiveName] = archive;
        source.Content[checksumName] = Encoding.UTF8.GetBytes($"{hash}  {archiveName}");
        return source;
    }

    public async Task<UpdateRelease?> GetLatestAsync(StableVersion runningVersion, CancellationToken cancellationToken)
    {
        DiscoveryCount++;
        if (DiscoveryGate is not null) await DiscoveryGate.Task.WaitAsync(cancellationToken);
        if (DiscoveryException is not null) throw DiscoveryException;
        return Release is not null && Release.Version.CompareTo(runningVersion) > 0 ? Release : null;
    }

    public async Task DownloadAsync(UpdateAsset asset, string destinationPath, long maximumBytes, CancellationToken cancellationToken)
    {
        var bytes = Content[asset.Name];
        var reportedSize = asset.Name.EndsWith(".zip", StringComparison.Ordinal) ? ReportedArchiveSize : null;
        if ((reportedSize ?? bytes.LongLength) > maximumBytes) throw new InvalidDataException("oversized");
        await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
    }

    public void Dispose()
    {
    }
}
