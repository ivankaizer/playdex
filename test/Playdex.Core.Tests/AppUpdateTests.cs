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
    public async Task StagingVerifiesTheChecksumAndWritesAManifest()
    {
        var directory = NewTempDir();
        var zipPath = Zip(directory, "source.zip", ("Playdex.exe", "new app"), ("Playdex.Run.exe", "new helper"));
        var zipBytes = await File.ReadAllBytesAsync(zipPath);
        var name = "playdex-windows-v2.1.0.zip";
        var goodHash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
        var release = new Release(new ReleaseVersion(2, 1, 0),
        [
            new ReleaseAsset(name, new Uri($"{Feed}/{name}"), zipBytes.Length),
            new ReleaseAsset(name + ".sha256", new Uri($"{Feed}/{name}.sha256"), null),
        ]);
        var install = Path.Combine(directory, "install");
        Directory.CreateDirectory(install);

        using (var bad = new ReleaseFeed(new AssetHandler(zipBytes, $"{new string('0', 64)}  {name}")))
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => AppUpdate.StageAsync(bad, release, install, Path.Combine(directory, "updates")));
        }
        using var good = new ReleaseFeed(new AssetHandler(zipBytes, $"{goodHash}  {name}"));
        var manifestPath = await AppUpdate.StageAsync(good, release, install, Path.Combine(directory, "updates"));

        var manifest = AppUpdate.Load(manifestPath);
        Assert.Equal("2.1.0", manifest.Version);
        Assert.Equal(goodHash, manifest.ArchiveHash);
        Assert.Equal("new app", await File.ReadAllTextAsync(Path.Combine(manifest.StagedDirectory, "Playdex.exe")));
        Assert.Equal(Path.GetFullPath(install), manifest.TargetDirectory);
    }

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

    private sealed class AssetHandler(byte[] archive, string checksum) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal)
                    ? new ByteArrayContent(archive)
                    : new StringContent(checksum),
            });
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
