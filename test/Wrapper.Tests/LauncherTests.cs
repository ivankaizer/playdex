using System.Diagnostics;
using System.Text.Json;
using Wrapper;
using static Wrapper.Tests.TestPaths;

namespace Wrapper.Tests;

public sealed class LauncherTests
{
    [Fact]
    public Task TestRemoveLauncherArtifacts()
    {
        var directory = NewTempDir();
        var sourceExecutable = Path.Combine(directory, "published-wrapper.exe");
        var gameExecutable = Path.Combine(directory, "actual-game.exe");
        File.WriteAllBytes(sourceExecutable, [0x4d, 0x5a, 0x01]);
        File.WriteAllBytes(gameExecutable, [0x4d, 0x5a, 0x02]);
        var storage = new StorageConfig { LauncherDirectory = Path.Combine(directory, "launchers") };
        var game = new GameConfig
        {
            GameId = "demo",
            Launch = new LaunchConfig { ExecutablePath = gameExecutable }
        };
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        var manager = new LauncherManager(logging.CreateLogger<LauncherManager>());
        var launcher = manager.SynchronizeGame(storage, game, Path.Combine(directory, "config.json"), sourceExecutable);
        var manifest = LauncherManifest.GetPath(launcher);

        manager.RemoveLauncherArtifacts(storage, game);

        Assert.True(!File.Exists(launcher));
        Assert.True(!File.Exists(manifest));
        Assert.True(File.Exists(gameExecutable));
        Assert.True(File.Exists(sourceExecutable));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestLauncherManifestRouting()
    {
        var directory = NewTempDir();
        var launcherPath = Path.Combine(directory, $"{AppBrand.LauncherPrefix}demo.exe");
        var configPath = Path.Combine(directory, "wrapper.json");
        File.WriteAllText(
            LauncherManifest.GetPath(launcherPath),
            JsonSerializer.Serialize(new LauncherManifest(configPath, "demo"), JsonOptions.Create()));

        var manifest = LauncherRouting.Resolve(launcherPath) ?? throw new InvalidOperationException("Launcher manifest was not resolved.");

        Assert.Equal(configPath, manifest.ConfigPath);
        Assert.Equal("demo", manifest.GameId);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestInvalidLauncherManifest()
    {
        var directory = NewTempDir();
        var missingManifestLauncher = Path.Combine(directory, $"{AppBrand.LauncherPrefix}missing.exe");
        try
        {
            LauncherRouting.Resolve(missingManifestLauncher);
            throw new InvalidOperationException("Expected missing launcher manifest failure.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("manifest is missing", StringComparison.OrdinalIgnoreCase))
        {
        }

        var invalidManifestLauncher = Path.Combine(directory, $"{AppBrand.LauncherPrefix}invalid.exe");
        File.WriteAllText(LauncherManifest.GetPath(invalidManifestLauncher), "{not-json");
        try
        {
            LauncherRouting.Resolve(invalidManifestLauncher);
            throw new InvalidOperationException("Expected invalid launcher manifest failure.");
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("is invalid", StringComparison.OrdinalIgnoreCase))
        {
        }

        return Task.CompletedTask;
    }

    [Fact]
    public Task TestLauncherPath()
    {
        var directory = NewTempDir();
        var storage = new StorageConfig { LauncherDirectory = directory };
        var game = new GameConfig { GameId = "demo/game", DisplayName = "First title" };
        var first = LauncherManager.GetLauncherPath(storage, game);
        game.DisplayName = "Renamed title";
        var second = LauncherManager.GetLauncherPath(storage, game);
        var different = LauncherManager.GetLauncherPath(storage, new GameConfig { GameId = "demo_game" });

        Assert.Equal(first, second);
        Assert.True(!string.Equals(first, different, StringComparison.OrdinalIgnoreCase));
        Assert.StartsWith(
            LauncherRouting.GeneratedLauncherPrefix,
            Path.GetFileName(first),
            StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestLauncherSynchronization()
    {
        var directory = NewTempDir();
        var sourceExecutable = Path.Combine(directory, "published-wrapper.exe");
        File.WriteAllBytes(sourceExecutable, [0x4d, 0x5a, 0x01, 0x02]);
        var configPath = Path.Combine(directory, "wrapper.json");
        var config = new AppConfig
        {
            Api = new ApiConfig { ApiKey = "global-secret" },
            Storage = new StorageConfig { LauncherDirectory = Path.Combine(directory, "launchers") }
        };
        var game = config.GetOrCreateGame("demo");
        game.DisplayName = "Demo";
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        var manager = new LauncherManager(logging.CreateLogger<LauncherManager>());

        var first = manager.SynchronizeAll(config, configPath, sourceExecutable).Single();
        var second = manager.SynchronizeAll(config, configPath, sourceExecutable).Single();
        var manifestPath = LauncherManifest.GetPath(first);
        var manifestText = File.ReadAllText(manifestPath);
        var manifest = LauncherManifest.Load(manifestPath);

        Assert.Equal(first, second);
        Assert.True(File.Exists(first));
        Assert.Equal(Path.GetFullPath(configPath), manifest.ConfigPath);
        Assert.Equal("demo", manifest.GameId);
        Assert.True(!manifestText.Contains("global-secret", StringComparison.Ordinal));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestMultiFileLauncherSource()
    {
        var directory = NewTempDir();
        var publishDirectory = Path.Combine(directory, "publish");
        var singleFileDirectory = Path.Combine(directory, "single-file");
        Directory.CreateDirectory(publishDirectory);
        Directory.CreateDirectory(singleFileDirectory);
        var oldExecutable = Path.Combine(publishDirectory, "Wrapper.exe");
        var oldAssembly = Path.Combine(publishDirectory, "Wrapper.dll");
        var singleFileExecutable = Path.Combine(singleFileDirectory, "Wrapper.exe");
        File.WriteAllBytes(oldExecutable, [0x01]);
        File.WriteAllBytes(oldAssembly, [0x02]);
        File.WriteAllBytes(singleFileExecutable, [0x4d, 0x5a, 0x03]);

        var resolved = LauncherManager.ResolveSingleFileSource(oldExecutable);
        var storage = new StorageConfig { LauncherDirectory = Path.Combine(directory, "launchers") };
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        var launcher = new LauncherManager(logging.CreateLogger<LauncherManager>()).SynchronizeGame(
            storage,
            new GameConfig { GameId = "demo" },
            Path.Combine(directory, "config.json"),
            oldExecutable);

        Assert.Equal(singleFileExecutable, resolved);
        Assert.True(File.ReadAllBytes(launcher).SequenceEqual(File.ReadAllBytes(singleFileExecutable)));
        return Task.CompletedTask;
    }

    [Fact]
    public Task TestDebugLauncherSource()
    {
        var directory = NewTempDir();
        var projectDirectory = Path.Combine(directory, "Wrapper");
        var debugDirectory = Path.Combine(projectDirectory, "bin", "Debug", "net10.0-windows");
        var singleFileDirectory = Path.Combine(projectDirectory, "bin", "Release", "net10.0-windows", "win-x64", "single-file");
        Directory.CreateDirectory(debugDirectory);
        Directory.CreateDirectory(singleFileDirectory);
        var debugExecutable = Path.Combine(debugDirectory, "Wrapper.exe");
        File.WriteAllBytes(debugExecutable, [0x01]);
        File.WriteAllBytes(Path.ChangeExtension(debugExecutable, ".dll"), [0x02]);
        var singleFileExecutable = Path.Combine(singleFileDirectory, "Wrapper.exe");
        File.WriteAllBytes(singleFileExecutable, [0x4d, 0x5a, 0x03]);

        Assert.Equal(singleFileExecutable, LauncherManager.ResolveSingleFileSource(debugExecutable));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TestPublishedLauncherIntegration()
    {
        var publishedWrapper = Environment.GetEnvironmentVariable(AppBrand.PublishedWrapperEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(publishedWrapper))
        {
            return;
        }

        var directory = NewTempDir();
        var configPath = Path.Combine(directory, "wrapper.json");
        var config = new AppConfig
        {
            Api = new ApiConfig { BaseUrl = "" },
            Storage = new StorageConfig
            {
                LauncherDirectory = Path.Combine(directory, "launchers"),
                StateDirectory = Path.Combine(directory, "state"),
                LogDirectory = Path.Combine(directory, "logs")
            }
        };
        var game = config.GetOrCreateGame("integration");
        game.DisplayName = "Integration Game";
        game.Launch.ExecutablePath = TestProcess.ExecutablePath;
        game.Launch.Arguments = [.. TestProcess.Arguments];
        game.Monitor.ExitDebounceSeconds = 1;
        await new ConfigStore(configPath).SaveAsync(config);

        using var logging = AppLoggerFactory.CreateInDirectory(config.Storage.LogDirectory);
        var launcher = new LauncherManager(logging.CreateLogger<LauncherManager>())
            .SynchronizeGame(config.Storage, game, configPath, publishedWrapper);
        using var process = Process.Start(new ProcessStartInfo(launcher)
        {
            UseShellExecute = false
        }) ?? throw new InvalidOperationException("Published launcher did not start.");

        await Task.Delay(500);
        Assert.True(!process.HasExited);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await process.WaitForExitAsync(timeout.Token);
        Assert.Equal(0, process.ExitCode);
    }
}
