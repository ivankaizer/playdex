namespace Wrapper;

internal enum BackupResultKind
{
    Never,
    Running,
    Success,
    Unsupported,
    Failed
}

internal sealed class GameBackupState
{
    public BackupResultKind Result { get; set; } = BackupResultKind.Never;
    public string? ResolvedTitle { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
}

internal sealed class BackupState
{
    public Dictionary<string, GameBackupState> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class BackupStateStore(string path) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = JsonOptions.Create();

    public string PathToState { get; } = path;

    public async Task<BackupState> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(PathToState))
        {
            return new BackupState();
        }

        await using var stream = File.OpenRead(PathToState);
        var state = await JsonSerializer.DeserializeAsync<BackupState>(stream, Options, cancellationToken) ?? new BackupState();
        state.Games = new Dictionary<string, GameBackupState>(
            state.Games ?? new Dictionary<string, GameBackupState>(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var game in state.Games.Values)
        {
            if (game.Result == BackupResultKind.Running)
            {
                game.Result = BackupResultKind.Failed;
                game.LastError = "The wrapper stopped before the backup completed.";
            }
        }
        return state;
    }

    public async Task SaveAsync(BackupState state, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(PathToState);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = $"{PathToState}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                File.Move(temporaryPath, PathToState, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryPath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();
}

internal sealed record LudusaviProcessResult(int ExitCode, string StandardOutput, string StandardError);

internal interface ILudusaviProcess
{
    Task<LudusaviProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

internal sealed class LudusaviProcess : ILudusaviProcess
{
    public async Task<LudusaviProcessResult> RunAsync(string executablePath, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            WorkingDirectory = Path.GetDirectoryName(executablePath)
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Ludusavi did not start.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }
        return new LudusaviProcessResult(process.ExitCode, await standardOutput, await standardError);
    }
}

internal sealed record LudusaviBackupResult(BackupResultKind Kind, string? ResolvedTitle, string? Error)
{
    public bool IsSuccess => Kind == BackupResultKind.Success;
}

internal sealed class LudusaviRunner
{
    private static readonly TimeSpan DefaultValidationTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultDiscoveryTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DefaultBackupTimeout = TimeSpan.FromHours(2);
    private readonly ILudusaviProcess process;
    private readonly TimeSpan validationTimeout;
    private readonly TimeSpan discoveryTimeout;
    private readonly TimeSpan backupTimeout;

    public LudusaviRunner(
        ILudusaviProcess process,
        TimeSpan? validationTimeout = null,
        TimeSpan? discoveryTimeout = null,
        TimeSpan? backupTimeout = null)
    {
        this.process = process;
        this.validationTimeout = validationTimeout ?? DefaultValidationTimeout;
        this.discoveryTimeout = discoveryTimeout ?? DefaultDiscoveryTimeout;
        this.backupTimeout = backupTimeout ?? DefaultBackupTimeout;
    }

    public async Task<string> ValidateAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        executablePath = LudusaviConfig.NormalizeExecutablePath(executablePath);
        var result = await RunWithTimeoutAsync(executablePath, ["--version"], validationTimeout, "Ludusavi validation", cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(FormatFailure("Ludusavi validation", result));
        }
        return result.StandardOutput.Trim();
    }

    public Task<LudusaviBackupResult> BackupAsync(LudusaviConfig config, GameConfig game, CancellationToken cancellationToken) =>
        BackupAsync(config, game, titleResolved: null, cancellationToken);

    public async Task<LudusaviBackupResult> BackupAsync(
        LudusaviConfig config,
        GameConfig game,
        Func<string, CancellationToken, Task>? titleResolved,
        CancellationToken cancellationToken)
    {
        string executablePath;
        try
        {
            executablePath = LudusaviConfig.NormalizeExecutablePath(config.ExecutablePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new LudusaviBackupResult(BackupResultKind.Failed, null, ex.Message);
        }

        var title = game.Backup.LudusaviGameTitle?.Trim();
        var steamId = 0u;
        if (string.IsNullOrWhiteSpace(title) &&
            (!string.Equals(game.GameProvider, "steam", StringComparison.OrdinalIgnoreCase) ||
             !uint.TryParse(game.ApiGameId, out steamId) || steamId == 0))
        {
            return new LudusaviBackupResult(BackupResultKind.Unsupported, null, "A valid Steam app ID is required.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            LudusaviProcessResult find;
            try
            {
                find = await RunWithTimeoutAsync(
                    executablePath,
                    ["--no-manifest-update", "find", "--steam-id", steamId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--backup", "--api"],
                    discoveryTimeout,
                    "Ludusavi game discovery",
                    cancellationToken);
            }
            catch (TimeoutException ex)
            {
                return new LudusaviBackupResult(BackupResultKind.Failed, null, ex.Message);
            }
            if (find.ExitCode != 0)
            {
                return new LudusaviBackupResult(BackupResultKind.Unsupported, null, FormatFailure("Ludusavi could not find this Steam game", find));
            }

            title = TryReadFirstGameTitle(find.StandardOutput);
            if (string.IsNullOrWhiteSpace(title))
            {
                return new LudusaviBackupResult(BackupResultKind.Failed, null, "Ludusavi returned no game title.");
            }

            if (titleResolved is not null)
            {
                await titleResolved(title, cancellationToken);
            }
        }

        LudusaviProcessResult backup;
        try
        {
            backup = await RunWithTimeoutAsync(
                executablePath,
                ["backup", title, "--cloud-sync", "--force", "--api"],
                backupTimeout,
                "Ludusavi backup",
                cancellationToken);
        }
        catch (TimeoutException ex)
        {
            return new LudusaviBackupResult(BackupResultKind.Failed, title, ex.Message);
        }
        if (backup.ExitCode != 0)
        {
            return new LudusaviBackupResult(BackupResultKind.Failed, title, FormatFailure("Ludusavi backup failed", backup));
        }

        try
        {
            using var _ = JsonDocument.Parse(backup.StandardOutput);
        }
        catch (JsonException)
        {
            return new LudusaviBackupResult(BackupResultKind.Failed, title, "Ludusavi returned invalid backup JSON.");
        }

        if (HasApiErrors(backup.StandardOutput))
        {
            return new LudusaviBackupResult(BackupResultKind.Failed, title, FormatFailure("Ludusavi backup failed", backup));
        }

        return new LudusaviBackupResult(BackupResultKind.Success, title, null);
    }

    internal static string? TryReadFirstGameTitle(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("games", out var games))
            {
                if (games.ValueKind == JsonValueKind.Object)
                {
                    return games.EnumerateObject().Select(property => property.Name).FirstOrDefault();
                }
                if (games.ValueKind == JsonValueKind.Array)
                {
                    foreach (var game in games.EnumerateArray())
                    {
                        if (game.ValueKind == JsonValueKind.String) return game.GetString();
                        if (game.ValueKind == JsonValueKind.Object && game.TryGetProperty("name", out var name)) return name.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
        }
        return null;
    }

    private static bool HasApiErrors(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("errors", out var errors) &&
                   errors.ValueKind == JsonValueKind.Array &&
                   errors.GetArrayLength() > 0;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static string FormatFailure(string prefix, LudusaviProcessResult result)
    {
        var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
        detail = detail.Trim();
        if (detail.Length > 300) detail = detail[..300];
        return string.IsNullOrWhiteSpace(detail) ? $"{prefix} (exit code {result.ExitCode})." : $"{prefix}: {detail}";
    }

    private async Task<LudusaviProcessResult> RunWithTimeoutAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string operation,
        CancellationToken cancellationToken)
    {
        using var timeoutCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellation.CancelAfter(timeout);
        try
        {
            return await process.RunAsync(executablePath, arguments, timeoutCancellation.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCancellation.IsCancellationRequested)
        {
            throw new TimeoutException($"{operation} timed out after {timeout.TotalSeconds:0} seconds.");
        }
    }
}

internal sealed partial class BackupCoordinator : IDisposable
{
    private static readonly TimeSpan SchedulerInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromHours(1);
    private readonly WrapperConfig config;
    private readonly ConfigStore configStore;
    private readonly BackupState state;
    private readonly BackupStateStore store;
    private readonly LudusaviRunner runner;
    private readonly ILogger<BackupCoordinator> logger;
    private readonly Action stateChanged;
    private readonly SemaphoreSlim operationGate = new(1, 1);
    private readonly HashSet<string> pendingGames = new(StringComparer.OrdinalIgnoreCase);
    private readonly object pendingLock = new();
    private readonly CancellationTokenSource cancellation = new();

    public BackupCoordinator(WrapperConfig config, ConfigStore configStore, BackupState state, BackupStateStore store, LudusaviRunner runner, ILogger<BackupCoordinator> logger, Action stateChanged)
    {
        this.config = config;
        this.configStore = configStore;
        this.state = state;
        this.store = store;
        this.runner = runner;
        this.logger = logger;
        this.stateChanged = stateChanged;
    }

    public GameBackupState? GetState(string gameId) => state.Games.GetValueOrDefault(gameId);

    public Task RequestManualAsync(GameConfig game) => QueueAsync(game, scheduled: false, cancellation.Token);

    public async Task RemoveStateAsync(string gameId)
    {
        if (state.Games.Remove(gameId))
        {
            await store.SaveAsync(state, cancellation.Token);
            LogBackupStateRemoved(logger, gameId);
        }
    }

    public async Task RunSchedulerAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await RunDueOnceAsync(DateTimeOffset.Now, cancellation.Token);
                await Task.Delay(SchedulerInterval, cancellation.Token);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    internal async Task RunDueOnceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!config.Ludusavi.IsConfigured) return;
        foreach (var game in config.Games.Values.Where(game => game.Backup.DailyEnabled))
        {
            var gameState = GetState(game.GameId);
            if (!IsDue(gameState, now)) continue;
            if (GameLock.IsHeld(config.Storage.StateDirectory, game.GameId))
            {
                LogBackupDeferred(logger, game.GameId);
                continue;
            }
            await QueueAsync(game, scheduled: true, cancellationToken);
        }
    }

    internal static bool IsDue(GameBackupState? state, DateTimeOffset now)
    {
        if (state?.LastSuccessAt?.ToLocalTime().Date == now.ToLocalTime().Date) return false;
        return state?.LastAttemptAt is null || now - state.LastAttemptAt >= FailureRetryDelay;
    }

    private async Task QueueAsync(GameConfig game, bool scheduled, CancellationToken cancellationToken)
    {
        var trigger = scheduled ? "scheduled" : "manual";
        lock (pendingLock)
        {
            if (!pendingGames.Add(game.GameId))
            {
                LogDuplicateBackupSuppressed(logger, game.GameId, trigger);
                return;
            }
        }

        LogBackupQueued(logger, game.GameId, trigger);
        try
        {
            await operationGate.WaitAsync(cancellationToken);
            try
            {
                if (scheduled && GameLock.IsHeld(config.Storage.StateDirectory, game.GameId))
                {
                    LogBackupDeferred(logger, game.GameId);
                    return;
                }
                var gameState = state.Games.GetValueOrDefault(game.GameId) ?? new GameBackupState();
                state.Games[game.GameId] = gameState;
                gameState.Result = BackupResultKind.Running;
                gameState.LastAttemptAt = DateTimeOffset.Now;
                gameState.LastError = null;
                await store.SaveAsync(state, cancellationToken);
                LogBackupStateChanged(logger, game.GameId, BackupResultKind.Running, trigger, gameState.ResolvedTitle, gameState.LastError);
                stateChanged();

                var result = await runner.BackupAsync(
                    config.Ludusavi,
                    game,
                    async (title, token) =>
                    {
                        game.Backup.LudusaviGameTitle = title;
                        await configStore.SaveAsync(config, token);
                        LogLudusaviTitleSaved(logger, game.GameId, title);
                    },
                    cancellationToken);
                gameState.Result = result.Kind;
                gameState.ResolvedTitle = result.ResolvedTitle ?? gameState.ResolvedTitle;
                gameState.LastError = result.Error;
                if (result.IsSuccess) gameState.LastSuccessAt = DateTimeOffset.Now;
                await store.SaveAsync(state, cancellationToken);
                LogBackupStateChanged(logger, game.GameId, result.Kind, trigger, gameState.ResolvedTitle, gameState.LastError);
                stateChanged();
            }
            finally
            {
                operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var gameState = state.Games.GetValueOrDefault(game.GameId) ?? new GameBackupState();
            state.Games[game.GameId] = gameState;
            gameState.Result = BackupResultKind.Failed;
            gameState.LastError = "Backup was cancelled.";
            await store.SaveAsync(state, CancellationToken.None);
            LogBackupCancelled(logger, game.GameId, trigger);
            LogBackupStateChanged(logger, game.GameId, gameState.Result, trigger, gameState.ResolvedTitle, gameState.LastError);
            stateChanged();
        }
        catch (Exception ex)
        {
            var gameState = state.Games.GetValueOrDefault(game.GameId) ?? new GameBackupState();
            state.Games[game.GameId] = gameState;
            gameState.Result = BackupResultKind.Failed;
            gameState.LastAttemptAt = DateTimeOffset.Now;
            gameState.LastError = ex.Message;
            await store.SaveAsync(state, CancellationToken.None);
            LogBackupFailed(logger, game.GameId, ex);
            stateChanged();
        }
        finally
        {
            lock (pendingLock) pendingGames.Remove(game.GameId);
        }
    }

    public void Dispose()
    {
        cancellation.Cancel();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Ludusavi backup queued for {GameId}; trigger={Trigger}")]
    private static partial void LogBackupQueued(ILogger logger, string gameId, string trigger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Error, Message = "Ludusavi backup failed for {GameId}")]
    private static partial void LogBackupFailed(ILogger logger, string gameId, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Ludusavi backup state for {GameId}: state={State}; trigger={Trigger}; title={ResolvedTitle}; error={Error}")]
    private static partial void LogBackupStateChanged(ILogger logger, string gameId, BackupResultKind state, string trigger, string? resolvedTitle, string? error);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Ludusavi scheduled backup deferred while game is active: {GameId}")]
    private static partial void LogBackupDeferred(ILogger logger, string gameId);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Ludusavi duplicate backup request suppressed for {GameId}; trigger={Trigger}")]
    private static partial void LogDuplicateBackupSuppressed(ILogger logger, string gameId, string trigger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Ludusavi backup cancelled for {GameId}; trigger={Trigger}")]
    private static partial void LogBackupCancelled(ILogger logger, string gameId, string trigger);

    [LoggerMessage(EventId = 7, Level = LogLevel.Information, Message = "Removed persisted Ludusavi backup state for {GameId}")]
    private static partial void LogBackupStateRemoved(ILogger logger, string gameId);

    [LoggerMessage(EventId = 8, Level = LogLevel.Information, Message = "Saved Ludusavi game title for {GameId}: {LudusaviGameTitle}")]
    private static partial void LogLudusaviTitleSaved(ILogger logger, string gameId, string ludusaviGameTitle);
}
