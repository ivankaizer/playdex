using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Playdex.Core;

/// <summary>
/// Runs one configured game: launches it, follows it to exit (<see cref="ProcessMonitor"/>) and records the session
/// (<see cref="SessionRecorder"/>). Holds the game's <see cref="GameLock"/>, so a game is never tracked twice.
/// </summary>

public sealed partial class SessionCoordinator : IDisposable
{
    private readonly AppConfig config;
    private readonly GameConfig game;
    private readonly ILogger<SessionCoordinator> logger;
    private readonly CaptureClient? api;
    private readonly SessionQueue? queue;
    private readonly GameLock gameLock;

    private SessionCoordinator(AppConfig config, GameConfig game, ILogger<SessionCoordinator> logger, string? queueFile, HttpMessageHandler? handler)
    {
        this.config = config;
        this.game = game;
        this.logger = logger;

        if (config.Api.IsEnabled)
        {
            api = new CaptureClient(config.Api, handler);
            queue = new SessionQueue(api, queueFile);
        }
        gameLock = GameLock.Acquire(config.Storage.StateDirectory, game.GameId);
    }

    /// <param name="queueFile">The offline queue; null for the install's, shared with the launcher.</param>
    /// <param name="handler">For tests; null sends real requests.</param>
    public static SessionCoordinator Create(
        AppConfig config, GameConfig game, ILogger<SessionCoordinator> logger, string? queueFile = null, HttpMessageHandler? handler = null) =>
        new(config, game, logger, queueFile, handler);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(game.Launch.ExecutablePath))
        {
            LogMissingExecutable(logger, game.GameId);
            throw new InvalidOperationException("Game executable path is not configured. Open Playdex settings to configure it.");
        }

        using var process = LaunchGame(game);
        if (api is null || queue is null)
        {
            LogApiTrackingDisabled(logger, game.GameId);
            await new ProcessMonitor(game.Monitor).WaitForExitAsync(process, cancellationToken);
            return;
        }

        // Start, a ping every interval, then end; or the whole session to the offline queue if start failed.
        using var recorder = new SessionRecorder(
            api, queue, CaptureGame.From(game, config.Api), TimeSpan.FromSeconds(Math.Max(1, config.Api.PingIntervalSeconds)));
        recorder.Start();
        DateTimeOffset? exitedAt = null;
        try
        {
            exitedAt = await new ProcessMonitor(game.Monitor).WaitForExitAsync(process, cancellationToken);
        }
        finally
        {
            var finished = await recorder.FinishAsync(exitedAt);
            LogSessionFinished(logger, game.GameId, finished);
        }
    }

    private Process LaunchGame(GameConfig selectedGame)
    {
        LogGameLaunching(logger, selectedGame.GameId, selectedGame.Launch.ExecutablePath);
        return GameProcess.Start(selectedGame);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Game {GameId} has no executable configured")]
    private static partial void LogMissingExecutable(ILogger logger, string gameId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "API tracking disabled for {GameId}")]
    private static partial void LogApiTrackingDisabled(ILogger logger, string gameId);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "Launching {GameId}: {ExecutablePath}")]
    private static partial void LogGameLaunching(ILogger logger, string gameId, string? executablePath);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Session of {GameId} finished: {Outcome}")]
    private static partial void LogSessionFinished(ILogger logger, string gameId, SessionOutcome outcome);

    public void Dispose()
    {
        queue?.Dispose();
        api?.Dispose();
        gameLock.Dispose();
    }
}
