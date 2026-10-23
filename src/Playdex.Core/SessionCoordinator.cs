using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Playdex.Core;

/// <summary>
/// Runs one configured game: launches it, follows it to exit (<see cref="ProcessMonitor"/>) and records the session
/// (<see cref="SessionRecorder"/>). Holds the game's <see cref="GameLock"/>, so a game is never tracked twice.
/// </summary>

public sealed partial class SessionCoordinator : IDisposable
{
    private readonly AppConfig _config;
    private readonly GameConfig _game;
    private readonly ILogger<SessionCoordinator> _logger;
    private readonly CaptureClient? _api;
    private readonly SessionQueue? _queue;
    private readonly GameLock _gameLock;

    private SessionCoordinator(AppConfig config, GameConfig game, ILogger<SessionCoordinator> logger, string? queueFile, HttpMessageHandler? handler)
    {
        this._config = config;
        this._game = game;
        this._logger = logger;

        if (config.Api.IsEnabled)
        {
            _api = new CaptureClient(config.Api, handler);
            _queue = new SessionQueue(_api, queueFile);
        }
        _gameLock = GameLock.Acquire(config.Storage.StateDirectory, game.GameId);
    }

    /// <param name="queueFile">The offline queue; null for the install's, shared with the launcher.</param>
    /// <param name="handler">For tests; null sends real requests.</param>
    public static SessionCoordinator Create(
        AppConfig config, GameConfig game, ILogger<SessionCoordinator> logger, string? queueFile = null, HttpMessageHandler? handler = null) =>
        new(config, game, logger, queueFile, handler);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_game.Launch.ExecutablePath))
        {
            LogMissingExecutable(_logger, _game.GameId);
            throw new InvalidOperationException("Game executable path is not configured. Open Playdex settings to configure it.");
        }

        using var started = LaunchGame(_game);
        if (_api is null || _queue is null)
        {
            LogApiTrackingDisabled(_logger, _game.GameId);
            await new ProcessMonitor(_game.Monitor).WaitForExitAsync(started, cancellationToken);
            return;
        }

        // Start, a ping every interval, then end; or the whole session to the offline queue if start failed.
        using var recorder = new SessionRecorder(
            _api, _queue, CaptureGame.From(_game, _config.Api), TimeSpan.FromSeconds(Math.Max(1, _config.Api.PingIntervalSeconds)));
        recorder.Start();
        DateTimeOffset? exitedAt = null;
        try
        {
            exitedAt = await new ProcessMonitor(_game.Monitor).WaitForExitAsync(started, cancellationToken);
        }
        finally
        {
            var finished = await recorder.FinishAsync(exitedAt);
            LogSessionFinished(_logger, _game.GameId, finished);
        }
    }

    private StartedGame LaunchGame(GameConfig selectedGame)
    {
        LogGameLaunching(_logger, selectedGame.GameId, selectedGame.Launch.ExecutablePath);
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
        _queue?.Dispose();
        _api?.Dispose();
        _gameLock.Dispose();
    }
}
