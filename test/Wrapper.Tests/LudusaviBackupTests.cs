using Microsoft.Extensions.Logging.Abstractions;
using Wrapper;
using static Wrapper.Tests.TestPaths;

namespace Wrapper.Tests;

public sealed class LudusaviBackupTests
{
    [Fact]
    public async Task TestBackupStateRoundTripAndInterruptedRecovery()
    {
        var path = Path.Combine(NewTempDir(), "state", "ludusavi-backups.json");
        using var store = new BackupStateStore(path);
        var state = new BackupState();
        state.Games["doom"] = new GameBackupState
        {
            Result = BackupResultKind.Running,
            LastAttemptAt = new DateTimeOffset(2026, 8, 9, 10, 0, 0, TimeSpan.Zero),
            ResolvedTitle = "Doom: The Dark Ages"
        };

        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();

        Assert.Equal(BackupResultKind.Failed, loaded.Games["DOOM"].Result);
        Assert.Equal("Doom: The Dark Ages", loaded.Games["doom"].ResolvedTitle);
        Assert.Contains("stopped", loaded.Games["doom"].LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task TestRunnerFindsBySteamIdAndBacksUpCanonicalTitle()
    {
        var executable = CreateLudusaviExecutable();
        var process = new RecordingLudusaviProcess(
            new(0, "{\"games\":{\"Doom: The Dark Ages\":{}}}", ""),
            new(0, "{\"errors\":[]}", ""));
        var runner = new LudusaviRunner(process);
        var game = new GameConfig { ApiGameId = "3017860", GameProvider = "steam" };

        var result = await runner.BackupAsync(new LudusaviConfig { ExecutablePath = executable }, game, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Doom: The Dark Ages", result.ResolvedTitle);
        Assert.Equal(["--no-manifest-update", "find", "--steam-id", "3017860", "--backup", "--api"], process.Calls[0].Arguments);
        Assert.Equal(["backup", "Doom: The Dark Ages", "--cloud-sync", "--force", "--api"], process.Calls[1].Arguments);
    }

    [Fact]
    public async Task TestRunnerRejectsUnsupportedAndInvalidOutput()
    {
        var executable = CreateLudusaviExecutable();
        var invalidGame = new GameConfig { ApiGameId = "not-steam", GameProvider = "steam" };
        var unusedProcess = new RecordingLudusaviProcess();
        var unsupported = await new LudusaviRunner(unusedProcess).BackupAsync(
            new LudusaviConfig { ExecutablePath = executable }, invalidGame, CancellationToken.None);
        Assert.Equal(BackupResultKind.Unsupported, unsupported.Kind);
        Assert.Empty(unusedProcess.Calls);

        var process = new RecordingLudusaviProcess(
            new(0, "{\"games\":{\"Doom\":{}}}", ""),
            new(0, "not-json", ""));
        var failed = await new LudusaviRunner(process).BackupAsync(
            new LudusaviConfig { ExecutablePath = executable },
            new GameConfig { ApiGameId = "3017860", GameProvider = "steam" },
            CancellationToken.None);
        Assert.Equal(BackupResultKind.Failed, failed.Kind);
        Assert.Contains("invalid", failed.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestValidationUsesVersionCommand()
    {
        var executable = CreateLudusaviExecutable();
        var process = new RecordingLudusaviProcess(new LudusaviProcessResult(0, "ludusavi 0.29.0", ""));
        var version = await new LudusaviRunner(process).ValidateAsync(executable);
        Assert.Equal("ludusavi 0.29.0", version);
        Assert.Equal(["--version"], process.Calls[0].Arguments);
    }

    [Fact]
    public async Task TestProcessClosesStandardInput()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await new LudusaviProcess().RunAsync(
            TestProcess.ExecutablePath,
            ["/d", "/c", "more"],
            cancellation.Token);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task TestDiscoveryTimeoutBecomesFailedState()
    {
        var executable = CreateLudusaviExecutable();
        var runner = new LudusaviRunner(
            new DelayedLudusaviProcess(),
            discoveryTimeout: TimeSpan.FromMilliseconds(25));

        var result = await runner.BackupAsync(
            new LudusaviConfig { ExecutablePath = executable },
            new GameConfig { ApiGameId = "3017860", GameProvider = "steam" },
            CancellationToken.None);

        Assert.Equal(BackupResultKind.Failed, result.Kind);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public Task TestDailyDueCalculation()
    {
        var now = new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);
        Assert.True(BackupCoordinator.IsDue(null, now));
        Assert.True(!BackupCoordinator.IsDue(new GameBackupState { LastSuccessAt = now.AddHours(-1) }, now));
        Assert.True(BackupCoordinator.IsDue(new GameBackupState { LastSuccessAt = now.AddDays(-1) }, now));
        Assert.True(!BackupCoordinator.IsDue(new GameBackupState { Result = BackupResultKind.Failed, LastAttemptAt = now.AddMinutes(-30) }, now));
        Assert.True(BackupCoordinator.IsDue(new GameBackupState { Result = BackupResultKind.Failed, LastAttemptAt = now.AddHours(-2) }, now));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TestScheduledBackupIsDeferredWhileGameLockIsHeld()
    {
        var directory = NewTempDir();
        var executable = CreateLudusaviExecutable();
        var game = new GameConfig
        {
            GameId = "doom",
            ApiGameId = "3017860",
            GameProvider = "steam",
            Backup = new BackupConfig { DailyEnabled = true }
        };
        var config = new WrapperConfig
        {
            Ludusavi = new LudusaviConfig { ExecutablePath = executable },
            Storage = new StorageConfig { StateDirectory = directory },
            Games = new Dictionary<string, GameConfig> { [game.GameId] = game }
        };
        var process = new RecordingLudusaviProcess(
            new(0, "{\"games\":{\"Doom: The Dark Ages\":{}}}", ""),
            new(0, "{\"errors\":[]}", ""));
        using var stateStore = new BackupStateStore(Path.Combine(directory, "backups.json"));
        using var coordinator = new BackupCoordinator(
            config,
            new ConfigStore(Path.Combine(directory, "config.json")),
            new BackupState(),
            stateStore,
            new LudusaviRunner(process),
            NullLogger<BackupCoordinator>.Instance,
            () => { });

        using (GameLock.Acquire(directory, game.GameId))
        {
            await coordinator.RunDueOnceAsync(DateTimeOffset.Now, CancellationToken.None);
        }
        Assert.Empty(process.Calls);

        await coordinator.RunDueOnceAsync(DateTimeOffset.Now, CancellationToken.None);
        Assert.Equal(2, process.Calls.Count);
        Assert.Equal(BackupResultKind.Success, coordinator.GetState(game.GameId)?.Result);
    }

    [Fact]
    public async Task TestBackupStateTransitionsAreLogged()
    {
        var directory = NewTempDir();
        var executable = CreateLudusaviExecutable();
        var game = new GameConfig
        {
            GameId = "doom",
            ApiGameId = "3017860",
            GameProvider = "steam"
        };
        var config = new WrapperConfig
        {
            Ludusavi = new LudusaviConfig { ExecutablePath = executable },
            Storage = new StorageConfig { StateDirectory = directory }
        };
        var process = new RecordingLudusaviProcess(
            new(0, "{\"games\":{\"Doom: The Dark Ages\":{}}}", ""),
            new(0, "{\"errors\":[]}", ""));
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        using var stateStore = new BackupStateStore(Path.Combine(directory, "backups.json"));
        using var coordinator = new BackupCoordinator(
            config,
            new ConfigStore(Path.Combine(directory, "config.json")),
            new BackupState(),
            stateStore,
            new LudusaviRunner(process),
            logging.CreateLogger<BackupCoordinator>(),
            () => { });

        await coordinator.RequestManualAsync(game);

        var contents = File.ReadAllText(logging.FilePath);
        Assert.Contains("backup queued for doom; trigger=manual", contents, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("state=Running; trigger=manual", contents, StringComparison.Ordinal);
        Assert.Contains("state=Success; trigger=manual", contents, StringComparison.Ordinal);
        Assert.Contains("title=Doom: The Dark Ages", contents, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestResolvedTitleIsPersistedAndReused()
    {
        var directory = NewTempDir();
        var executable = CreateLudusaviExecutable();
        var configPath = Path.Combine(directory, "config.json");
        var configStore = new ConfigStore(configPath);
        var game = new GameConfig
        {
            GameId = "doom",
            ApiGameId = "3017860",
            GameProvider = "steam"
        };
        var config = new WrapperConfig
        {
            Ludusavi = new LudusaviConfig { ExecutablePath = executable },
            Storage = new StorageConfig { StateDirectory = directory },
            Games = new Dictionary<string, GameConfig> { [game.GameId] = game }
        };
        await configStore.SaveAsync(config);
        var process = new RecordingLudusaviProcess(
            new(0, "{\"games\":{\"Doom: The Dark Ages\":{}}}", ""),
            new(0, "{\"errors\":[]}", ""),
            new(0, "{\"errors\":[]}", ""));
        using var stateStore = new BackupStateStore(Path.Combine(directory, "backups.json"));
        using var coordinator = new BackupCoordinator(
            config,
            configStore,
            new BackupState(),
            stateStore,
            new LudusaviRunner(process),
            NullLogger<BackupCoordinator>.Instance,
            () => { });

        await coordinator.RequestManualAsync(game);
        Assert.Equal("Doom: The Dark Ages", game.Backup.LudusaviGameTitle);
        var saved = await configStore.LoadOrCreateAsync();
        Assert.Equal("Doom: The Dark Ages", saved.Games[game.GameId].Backup.LudusaviGameTitle);

        await coordinator.RequestManualAsync(game);
        Assert.Equal(3, process.Calls.Count);
        Assert.Equal("find", process.Calls[0].Arguments[1]);
        Assert.Equal("backup", process.Calls[1].Arguments[0]);
        Assert.Equal("backup", process.Calls[2].Arguments[0]);
    }

    private static string CreateLudusaviExecutable()
    {
        var path = Path.Combine(NewTempDir(), "ludusavi.exe");
        File.WriteAllBytes(path, [0x4d, 0x5a]);
        return path;
    }

    private sealed class RecordingLudusaviProcess(params LudusaviProcessResult[] results) : ILudusaviProcess
    {
        private readonly Queue<LudusaviProcessResult> results = new(results);
        public List<(string ExecutablePath, IReadOnlyList<string> Arguments)> Calls { get; } = [];

        public Task<LudusaviProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Calls.Add((executablePath, arguments.ToArray()));
            return Task.FromResult(results.Dequeue());
        }
    }

    private sealed class DelayedLudusaviProcess : ILudusaviProcess
    {
        public async Task<LudusaviProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }
    }
}
