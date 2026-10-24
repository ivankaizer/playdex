using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using static Playdex.Core.Tests.TestPaths;

namespace Playdex.Core.Tests;

public sealed class AppUpdateTests
{
    private const string Feed = "https://download.playdex.dev/windows/v2.0.0";

    [Fact]
    public void VersionsParseFromStableTagsAndCompare()
    {
        Assert.True(ReleaseVersion.TryParseTag("v2.10.3", out var version));
        Assert.Equal(new ReleaseVersion(2, 10, 3), version);
        Assert.True(new ReleaseVersion(2, 10, 3) > new ReleaseVersion(2, 9, 9));
        Assert.True(new ReleaseVersion(1, 1, 0) < new ReleaseVersion(2, 0, 0));
        foreach (var tag in new[] { "2.0.0", "v2.0", "v02.0.0", "v2.0.0-beta", "v1.1", "1.0" })
        {
            Assert.False(ReleaseVersion.TryParseTag(tag, out _), tag);
        }
    }

    [Fact]
    public void OnlyStableReleasesWithUniqueAssetsCount()
    {
        static ReleaseDto Dto(string tag, bool draft = false, bool prerelease = false) => new()
        {
            TagName = tag,
            Draft = draft,
            Prerelease = prerelease,
            Assets =
            [
                new() { Name = "playdex-windows-v2.0.0.zip", BrowserDownloadUrl = $"{Feed}/a.zip", Size = 10 },
                new() { Name = "twice.zip", BrowserDownloadUrl = $"{Feed}/c.zip" },
                new() { Name = "twice.zip", BrowserDownloadUrl = $"{Feed}/d.zip" },
            ],
        };

        Assert.Null(ReleaseFeed.ToRelease(Dto("v2.0.0", draft: true)));
        Assert.Null(ReleaseFeed.ToRelease(Dto("v2.0.0", prerelease: true)));
        Assert.Null(ReleaseFeed.ToRelease(Dto("latest")));
        var release = ReleaseFeed.ToRelease(Dto("v2.0.0"))!;
        Assert.Equal(["playdex-windows-v2.0.0.zip"], release.Assets.Select(a => a.Name));
    }

    [Theory]
    [InlineData("http://download.playdex.dev/windows/v2.0.0/b.zip")]
    [InlineData("https://example.test/windows/v2.0.0/b.zip")]
    [InlineData("https://download.playdex.dev.example.test/windows/v2.0.0/b.zip")]
    [InlineData("https://download.playdex.dev./windows/v2.0.0/b.zip")]
    [InlineData("https://download.playdex.dev@example.test/windows/v2.0.0/b.zip")]
    [InlineData("https://user@download.playdex.dev/windows/v2.0.0/b.zip")]
    [InlineData("https://download.playdex.dev:8443/windows/v2.0.0/b.zip")]
    [InlineData("https://playdex.dev/windows/v2.0.0/b.zip")]
    [InlineData("ftp://download.playdex.dev/windows/v2.0.0/b.zip")]
    [InlineData("windows/v2.0.0/b.zip")]
    [InlineData("")]
    public void AReleaseWithAFileOffTheFeedIsRefused(string url)
    {
        var dto = new ReleaseDto
        {
            TagName = "v2.0.0",
            Assets =
            [
                new() { Name = "playdex-windows-v2.0.0.zip", BrowserDownloadUrl = $"{Feed}/a.zip", Size = 10 },
                new() { Name = "b.zip", BrowserDownloadUrl = url, Size = 10 },
            ],
        };

        var refused = Assert.Throws<InvalidDataException>(() => ReleaseFeed.ToRelease(dto));
        Assert.Contains("https://download.playdex.dev", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyHttpsOnTheFeedHostIsAFeedUrl()
    {
        Assert.Equal("download.playdex.dev", ReleaseFeed.BaseUri.Host);
        Assert.True(ReleaseFeed.IsFeedUri(new Uri($"{Feed}/playdex-windows-v2.0.0.zip")));
        Assert.True(ReleaseFeed.IsFeedUri(new Uri("https://DOWNLOAD.playdex.dev:443/windows/v2.0.0/a.zip")));
        Assert.False(ReleaseFeed.IsFeedUri(new Uri("http://download.playdex.dev/windows/v2.0.0/a.zip")));
        Assert.False(ReleaseFeed.IsFeedUri(new Uri("https://evil.example/windows/v2.0.0/a.zip")));
        Assert.False(ReleaseFeed.IsFeedUri(new Uri("windows/v2.0.0/a.zip", UriKind.Relative)));
    }

    [Fact]
    public async Task DownloadsOffTheFeedAreRefusedBeforeAnyRequest()
    {
        var handler = new FeedHandler("{}");
        using var feed = new ReleaseFeed(handler);
        var destination = Path.Combine(NewTempDir(), "a.zip");

        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => feed.DownloadAsync(
            new ReleaseAsset("a.zip", new Uri("https://evil.example/windows/v2.0.0/a.zip"), 10), destination, 1024));

        Assert.Contains("https://download.playdex.dev", refused.Message, StringComparison.Ordinal);
        Assert.Empty(handler.Requested);
        Assert.False(File.Exists(destination));
    }

    [Fact]
    public async Task ReleasesAreReadFromThePublicFeed()
    {
        const string json = """
            {"tag_name":"v2.1.0","draft":false,"prerelease":false,"assets":[
              {"name":"Playdex-Setup.exe","browser_download_url":"https://download.playdex.dev/windows/v2.1.0/Playdex-Setup.exe","size":42}]}
            """;
        var handler = new FeedHandler(json);
        using var feed = new ReleaseFeed(handler);

        var latest = await feed.LatestAsync();
        var tagged = await feed.ForVersionAsync(new ReleaseVersion(2, 1, 0));

        Assert.Equal(
            ["https://download.playdex.dev/windows/releases/latest.json", "https://download.playdex.dev/windows/releases/v2.1.0.json"],
            handler.Requested.Select(uri => uri.AbsoluteUri));
        Assert.Equal(new ReleaseVersion(2, 1, 0), latest!.Version);
        Assert.Equal(new Uri("https://download.playdex.dev/windows/v2.1.0/Playdex-Setup.exe"), tagged!.Asset("Playdex-Setup.exe")!.DownloadUri);
        Assert.Null(await new ReleaseFeed(new FeedHandler(null)).LatestAsync());
    }

    [Fact]
    public void ChecksumFilesNameTheArchive()
    {
        var hash = new string('a', 64);
        Assert.Equal(hash, AppUpdate.ParseChecksum($"{hash.ToUpperInvariant()}  playdex-windows-v2.0.0.zip\n", "playdex-windows-v2.0.0.zip"));
        Assert.Throws<InvalidDataException>(() => AppUpdate.ParseChecksum($"{hash}  other.zip", "playdex-windows-v2.0.0.zip"));
        Assert.Throws<InvalidDataException>(() => AppUpdate.ParseChecksum("nonsense", "playdex-windows-v2.0.0.zip"));
    }

    [Fact]
    public void ExtractionRefusesPathsOutsideTheFolderAndNeedsBothExecutables()
    {
        var directory = NewTempDir();
        var unsafeZip = Zip(directory, "unsafe.zip", ("../escape.txt", "x"), ("Playdex.exe", "x"), ("Playdex.Run.exe", "x"));
        Assert.Throws<InvalidDataException>(() => AppUpdate.Extract(unsafeZip, Path.Combine(directory, "a")));
        Assert.False(File.Exists(Path.Combine(directory, "escape.txt")));

        var incomplete = Zip(directory, "incomplete.zip", ("Playdex.exe", "x"));
        Assert.Throws<InvalidDataException>(() => AppUpdate.Extract(incomplete, Path.Combine(directory, "b")));

        var good = Zip(directory, "good.zip", ("Playdex.exe", "x"), ("Playdex.Run.exe", "x"), ("ui/index.html", "<html>"));
        AppUpdate.Extract(good, Path.Combine(directory, "c"));
        Assert.True(File.Exists(Path.Combine(directory, "c", "ui", "index.html")));
    }

    [Fact]
    public void ExtractionIsCappedInEntriesAndSizeBeforeAnythingIsWritten()
    {
        var directory = NewTempDir();
        var four = Zip(directory, "four.zip", ("Playdex.exe", "x"), ("Playdex.Run.exe", "x"), ("a.txt", "x"), ("b.txt", "x"));
        var tooMany = Assert.Throws<InvalidDataException>(() => AppUpdate.ExtractSafely(four, Path.Combine(directory, "a"), 1024, 3, "The update"));
        Assert.Contains("more than 3 files", tooMany.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(directory, "a")));
        AppUpdate.ExtractSafely(four, Path.Combine(directory, "b"), 1024, 4, "The update");
        Assert.Equal(4, Directory.GetFiles(Path.Combine(directory, "b")).Length);

        var big = Zip(directory, "big.zip", ("Playdex.exe", new string('x', 600)), ("Playdex.Run.exe", new string('x', 600)));
        var tooBig = Assert.Throws<InvalidDataException>(() => AppUpdate.ExtractSafely(big, Path.Combine(directory, "c"), 1000, 10, "The update"));
        Assert.Contains("larger than allowed", tooBig.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(directory, "c")));
        AppUpdate.ExtractSafely(big, Path.Combine(directory, "d"), 2000, 10, "The update");
        Assert.Equal(new string('x', 600), File.ReadAllText(Path.Combine(directory, "d", "Playdex.Run.exe")));
    }

    [Fact]
    public async Task StagingVerifiesTheSignatureAndTheChecksumAndWritesAManifest()
    {
        var directory = NewTempDir();
        var zipPath = Zip(directory, "source.zip", ("Playdex.exe", "new app"), ("Playdex.Run.exe", "new helper"));
        var zipBytes = await File.ReadAllBytesAsync(zipPath);
        var name = "playdex-windows-v2.1.0.zip";
        var goodHash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
        var release = SignedRelease(name, zipBytes.Length);
        var install = Path.Combine(directory, "install");
        Directory.CreateDirectory(install);
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        // A key added for a rotation sits next to the one in use: either is accepted.
        using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string[] keys = [PublicKey(next), PublicKey(signing)];

        var badChecksum = $"{new string('0', 64)}  {name}";
        using (var bad = new ReleaseFeed(new AssetHandler(zipBytes, badChecksum, Sign(signing, badChecksum))))
        {
            await Assert.ThrowsAsync<InvalidDataException>(
                () => AppUpdate.StageAsync(bad, release, install, Path.Combine(directory, "updates"), keys, CancellationToken.None));
        }
        var checksum = $"{goodHash}  {name}";
        using var good = new ReleaseFeed(new AssetHandler(zipBytes, checksum, Sign(signing, checksum)));
        var manifestPath = await AppUpdate.StageAsync(good, release, install, Path.Combine(directory, "updates"), keys, CancellationToken.None);

        var manifest = AppUpdate.Load(manifestPath);
        Assert.Equal("2.1.0", manifest.Version);
        Assert.Equal(goodHash, manifest.ArchiveHash);
        Assert.Equal("new app", await File.ReadAllTextAsync(Path.Combine(manifest.StagedDirectory, "Playdex.exe")));
        Assert.Equal(Path.GetFullPath(install), manifest.TargetDirectory);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("other key")]
    [InlineData("tampered")]
    [InlineData("not base64")]
    [InlineData("empty")]
    public async Task AnUpdateThatIsNotSignedIsRefusedBeforeItsArchiveIsDownloaded(string signature)
    {
        var directory = NewTempDir();
        var zipBytes = await File.ReadAllBytesAsync(Zip(directory, "source.zip", ("Playdex.exe", "app"), ("Playdex.Run.exe", "helper")));
        var name = "playdex-windows-v2.1.0.zip";
        var checksum = $"{Convert.ToHexStringLower(SHA256.HashData(zipBytes))}  {name}";
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var release = SignedRelease(name, zipBytes.Length);
        var served = signature switch
        {
            "other key" => Sign(other, checksum),
            // Signed for another archive (another release's checksum).
            "tampered" => Sign(signing, checksum.Replace("v2.1.0", "v2.0.9", StringComparison.Ordinal)),
            "not base64" => "not a signature!",
            "empty" => "",
            _ => Sign(signing, checksum),
        };
        if (signature == "missing")
        {
            release = release with { Assets = release.Assets.Where(a => !a.Name.EndsWith(".sig", StringComparison.Ordinal)).ToList() };
        }
        var handler = new AssetHandler(zipBytes, checksum, served);
        using var feed = new ReleaseFeed(handler);

        var refused = await Assert.ThrowsAsync<InvalidDataException>(
            () => AppUpdate.StageAsync(feed, release, directory, Path.Combine(directory, "updates"), [PublicKey(signing)], CancellationToken.None));

        Assert.Contains("signed", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(handler.Requested, uri => uri.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(directory, "updates", "v2.1.0")));
    }

    [Fact]
    public void TheReleasedSigningKeysAreP256PublicKeys()
    {
        Assert.NotEmpty(AppUpdate.UpdateSigningKeys);
        foreach (var key in AppUpdate.UpdateSigningKeys)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(key), out _);
            Assert.Equal(256, ecdsa.KeySize);
            // A public key only: nothing to sign with.
            Assert.ThrowsAny<CryptographicException>(() => ecdsa.ExportECPrivateKey());
        }
    }

    private static Release SignedRelease(string name, long size) => new(new ReleaseVersion(2, 1, 0),
    [
        new ReleaseAsset(name, new Uri($"{Feed}/{name}"), size),
        new ReleaseAsset(name + ".sha256", new Uri($"{Feed}/{name}.sha256"), null),
        new ReleaseAsset(name + ".sha256.sig", new Uri($"{Feed}/{name}.sha256.sig"), null),
    ]);

    /// <summary>What the release workflow writes to the .sig: the checksum file's bytes signed, as base64.</summary>
    private static string Sign(ECDsa key, string checksum) =>
        Convert.ToBase64String(key.SignData(Encoding.ASCII.GetBytes(checksum), HashAlgorithmName.SHA256));

    private static string PublicKey(ECDsa key) => Convert.ToBase64String(key.ExportSubjectPublicKeyInfo());

    [Fact]
    public async Task StartingPlaydexWaitsWhileTheHelperAppliesAnUpdate()
    {
        Assert.False(AppUpdate.WaitWhileApplying(TimeSpan.Zero));

        using var held = new ManualResetEventSlim();
        using var done = new ManualResetEventSlim();
        // The helper holds the lock on one thread for the whole swap, as Playdex.Run does.
        var helper = new Thread(() =>
        {
            using var applying = AppUpdate.HoldApplyingLock();
            held.Set();
            done.Wait();
            applying.ReleaseMutex();
        });
        helper.Start();
        held.Wait();

        var waiting = Task.Run(() => AppUpdate.WaitWhileApplying(TimeSpan.FromSeconds(10)));
        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(300)));
        done.Set();
        Assert.True(await waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        helper.Join();

        Assert.False(AppUpdate.WaitWhileApplying(TimeSpan.Zero));
    }

    [Fact]
    public async Task AVersionThatDoesNotStartIsRolledBack()
    {
        // Needs a real executable for the helper and the "apps": the published Playdex.Run.exe. Started with
        // --update-health it exits at once without confirming, which is exactly an update that doesn't start.
        var published = Environment.GetEnvironmentVariable("PLAYDEX_PUBLISHED_RUN");
        if (string.IsNullOrWhiteSpace(published))
        {
            return;
        }
        var directory = NewTempDir();
        var install = Path.Combine(directory, "Programs", "Playdex");
        var updates = Path.Combine(directory, "updates");
        var staged = Path.Combine(updates, "v9.9.9", "app");
        foreach (var (folder, marker) in new[] { (install, "old"), (staged, "new") })
        {
            Directory.CreateDirectory(folder);
            File.Copy(published, Path.Combine(folder, "Playdex.exe"));
            File.Copy(published, Path.Combine(folder, "Playdex.Run.exe"));
            await File.WriteAllTextAsync(Path.Combine(folder, "version.txt"), marker);
        }
        var manifestPath = Path.Combine(updates, "v9.9.9", "update.json");
        await File.WriteAllTextAsync(manifestPath, System.Text.Json.JsonSerializer.Serialize(
            new StagedUpdate("9.9.9", new string('a', 64), staged, install, updates), UpdateJson.Default.StagedUpdate));

        var start = AppUpdate.PrepareApply(manifestPath);
        // The helper waits for the app it replaces to exit; stand in a process that already has.
        using (var exited = Process.Start(new ProcessStartInfo(TestProcess.ExecutablePath, "/c exit") { CreateNoWindow = true, UseShellExecute = false })!)
        {
            await exited.WaitForExitAsync();
            start.ArgumentList[2] = exited.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        var started = Stopwatch.StartNew();
        using var helper = Process.Start(start)!;
        await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));

        Assert.Equal(1, helper.ExitCode);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(20), "the helper did not wait for a process that had exited");
        Assert.False(Directory.Exists(staged), "the new version was moved in before it was rolled back");
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(install, "version.txt")));
        Assert.Contains("could not be installed", await File.ReadAllTextAsync(Path.Combine(updates, "last-failure.txt")), StringComparison.Ordinal);
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(install)!, "Playdex.old-*"));
    }

    [Fact]
    public async Task ARollBackStopsTheNewVersionAndPutsThePreviousOneBack()
    {
        var (install, backup, manifest) = await RollBackSetupAsync();
        await File.WriteAllTextAsync(Path.Combine(install, "version.txt"), "new");
        // The new version still running: a process that would otherwise outlive the rollback (ping, about 2 s).
        using var running = OperatingSystem.IsWindows() ? TestProcess.Start() : null;

        var outcome = await AppUpdate.RollBackAsync(manifest, backup, running, new InvalidOperationException("The new version did not start properly."));

        Assert.Equal(UpdateOutcome.RolledBack, outcome);
        Assert.True(running?.HasExited ?? true, "the new version was not stopped before the restore");
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(install, "version.txt")));
        Assert.False(Directory.Exists(backup));
        Assert.Equal(
            "The update to 9.9.9 could not be installed: The new version did not start properly.",
            await File.ReadAllTextAsync(manifest.FailurePath!));
    }

    [Fact]
    public async Task ARollBackThatCannotPutThePreviousVersionBackIsAFailure()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // Only Windows refuses to delete a file that is open.
        }
        var (install, backup, manifest) = await RollBackSetupAsync();

        UpdateOutcome outcome;
        using (new FileStream(Path.Combine(install, "Playdex.exe"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            outcome = await AppUpdate.RollBackAsync(
                manifest, backup, updated: null, new InvalidOperationException("The new version did not start properly."), restoreAttempts: 2);
        }

        Assert.Equal(UpdateOutcome.RestoreFailed, outcome);
        Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(backup, "version.txt")));
        var failure = await File.ReadAllTextAsync(manifest.FailurePath!);
        Assert.Contains("could not be put back", failure, StringComparison.Ordinal);
        Assert.Contains(backup, failure, StringComparison.Ordinal);
    }

    /// <summary>An install folder holding the new version and the previous one moved aside, as the helper leaves them.</summary>
    private static async Task<(string Install, string Backup, StagedUpdate Manifest)> RollBackSetupAsync()
    {
        var directory = NewTempDir();
        var install = Directory.CreateDirectory(Path.Combine(directory, "Programs", "Playdex")).FullName;
        var backup = Directory.CreateDirectory(install + ".old-test").FullName;
        await File.WriteAllTextAsync(Path.Combine(backup, "version.txt"), "old");
        var updates = Path.Combine(directory, "updates");
        var manifest = new StagedUpdate(
            "9.9.9", new string('a', 64), Path.Combine(updates, "v9.9.9", "app"), install, updates,
            FailurePath: Path.Combine(updates, "last-failure.txt"));
        return (install, backup, manifest);
    }

    private static string Zip(string directory, string name, params (string Path, string Content)[] entries)
    {
        var path = Path.Combine(directory, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entryPath, content) in entries)
        {
            using var writer = new StreamWriter(zip.CreateEntry(entryPath).Open(), Encoding.UTF8);
            writer.Write(content);
        }
        return path;
    }

    private sealed class AssetHandler(byte[] archive, string checksum, string signature) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            Requested.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = path.EndsWith(".zip", StringComparison.Ordinal) ? new ByteArrayContent(archive)
                    : path.EndsWith(".sig", StringComparison.Ordinal) ? new StringContent(signature)
                    : new StringContent(checksum),
            });
        }
    }

    /// <summary>Serves <paramref name="json"/> for every feed request, or 404 when it is null.</summary>
    private sealed class FeedHandler(string? json) : HttpMessageHandler
    {
        public List<Uri> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requested.Add(request.RequestUri!);
            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
}
