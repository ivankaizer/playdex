using System.Net;
using System.Text.Json;
using Wrapper;
using static Wrapper.Tests.TestPaths;

namespace Wrapper.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task TestStartContract()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var config = new ApiConfig
        {
            BaseUrl = "https://example.test/api",
            ApiKey = "global-key",
            Platform = "pc",
            RequestTimeoutSeconds = 10
        };
        var game = new GameConfig
        {
            GameId = "wrapper-demo",
            ApiGameId = "1234",
            DisplayName = "Demo Game",
            GameProvider = "steam",
            Platform = "gog"
        };
        using var api = new CaptureClient(config, handler);

        Assert.True(await api.StartAsync(CaptureGame.From(game, config)));
        var request = handler.Requests.Single();
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://example.test/api/capture/sessions/start", request.Uri);
        Assert.Equal($"Bearer {config.ApiKey}", request.Authorization);

        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("Demo Game", json.RootElement.GetProperty("gameTitle").GetString());
        Assert.Equal("1234", json.RootElement.GetProperty("gameId").GetString());
        Assert.Equal("steam", json.RootElement.GetProperty("gameIdProvider").GetString());
        Assert.Equal("gog", json.RootElement.GetProperty("platform").GetString());
        Assert.True(!json.RootElement.TryGetProperty("gameProvider", out _));
    }

    [Fact]
    public Task TestCaptureGameDefaults()
    {
        var api = new ApiConfig { Platform = "steam" };
        var game = CaptureGame.From(new GameConfig { GameId = "demo", DisplayName = "Demo", Platform = null }, api);

        Assert.Equal("steam", game.Platform);
        Assert.Equal("demo", game.GameId);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TestPlatformCatalogContractAndFailure()
    {
        const string response = """
            {"data":[{"key":"pc","name":"PC"},{"key":"gog","name":"GOG"}],"error":null}
            """;
        var handler = new RecordingHandler(HttpStatusCode.OK, response);
        using var client = new PlatformCatalogClient(
            new ApiConfig { BaseUrl = "https://example.test/api" },
            handler);

        var loaded = await client.LoadAsync("key");

        Assert.True(loaded.IsAvailable);
        Assert.Equal(["pc", "gog"], loaded.Platforms.Select(platform => platform.Key));
        var request = handler.Requests.Single();
        Assert.Equal("GET", request.Method);
        Assert.Equal("https://example.test/api/capture/platforms", request.Uri);
        Assert.Equal("Bearer key", request.Authorization);

        using var failedClient = new PlatformCatalogClient(
            new ApiConfig { BaseUrl = "https://example.test/api" },
            new RecordingHandler(HttpStatusCode.ServiceUnavailable));
        var failed = await failedClient.LoadAsync("key");
        Assert.True(!failed.IsAvailable);
        Assert.Empty(failed.Platforms);
    }

    [Fact]
    public async Task TestSessionCoordinatorRecordsStartAndEnd()
    {
        var directory = NewTempDir();
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var config = new AppConfig
        {
            Api = new ApiConfig { BaseUrl = "https://example.test/api", ApiKey = "key" },
            Storage = new StorageConfig { StateDirectory = directory, LogDirectory = directory }
        };
        var game = config.GetOrCreateGame($"demo-{Guid.NewGuid():N}");
        game.Launch.ExecutablePath = TestProcess.ExecutablePath;
        game.Launch.Arguments = [.. TestProcess.Arguments];
        game.Monitor.ExitDebounceSeconds = 1;
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        using var coordinator = SessionCoordinator.Create(config, game, logging, Path.Combine(directory, "queue.jsonl"), handler);

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Equal(
            ["https://example.test/api/capture/sessions/start", "https://example.test/api/capture/sessions/end"],
            handler.Requests.Select(r => r.Uri));
        Assert.False(File.Exists(Path.Combine(directory, "queue.jsonl")));
    }

    [Fact]
    public async Task TestApiDisabledSession()
    {
        var directory = NewTempDir();
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        var config = new AppConfig
        {
            Api = new ApiConfig { BaseUrl = "" },
            Storage = new StorageConfig { StateDirectory = directory, LogDirectory = directory }
        };
        var game = config.GetOrCreateGame($"disabled-{Guid.NewGuid():N}");
        game.Launch.ExecutablePath = TestProcess.ExecutablePath;
        game.Launch.Arguments = [.. TestProcess.Arguments];
        game.Monitor.ExitDebounceSeconds = 1;

        using var coordinator = SessionCoordinator.Create(config, game, logging);
        await coordinator.RunAsync(CancellationToken.None);

        Assert.True(File.ReadAllText(logging.FilePath).Contains("api tracking disabled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task TestProcessMonitor()
    {
        using var process = TestProcess.Start();

        using var logging = AppLoggerFactory.CreateInDirectory(NewTempDir());
        var monitor = new ProcessMonitor(
            new MonitorConfig { Mode = MonitorMode.LaunchedProcess, ExitDebounceSeconds = 1 },
            logging.CreateLogger<ProcessMonitor>());
        await monitor.WaitForExitAsync(process, CancellationToken.None);
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task TestSessionCoordinatorQueuesWhenTheApiIsUnreachable()
    {
        var directory = NewTempDir();
        var gameId = $"demo-{Guid.NewGuid():N}";
        var queueFile = Path.Combine(directory, "queue.jsonl");
        var config = new AppConfig
        {
            Api = new ApiConfig
            {
                BaseUrl = "http://127.0.0.1:1/api",
                ApiKey = "test-key",
                PingIntervalSeconds = 1,
                RequestTimeoutSeconds = 1
            },
            Storage = new StorageConfig
            {
                StateDirectory = directory,
                LogDirectory = directory
            }
        };
        var game = config.GetOrCreateGame(gameId);
        game.Launch.ExecutablePath = TestProcess.ExecutablePath;
        game.Launch.Arguments = [.. TestProcess.Arguments];
        game.Monitor.ExitDebounceSeconds = 1;
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        using var coordinator = SessionCoordinator.Create(config, game, logging, queueFile);

        await coordinator.RunAsync(CancellationToken.None);

        // The API was unreachable, so start failed and the whole session waits in the offline queue.
        using var queued = JsonDocument.Parse(File.ReadAllLines(queueFile).Single());
        Assert.Equal(gameId, queued.RootElement.GetProperty("gameId").GetString());
        Assert.True(queued.RootElement.GetProperty("durationInSeconds").GetInt32() >= 1);
    }

    [Fact]
    public async Task TestMissingExecutableDoesNotCreateSession()
    {
        var directory = NewTempDir();
        var gameId = $"demo-{Guid.NewGuid():N}";
        var config = new AppConfig
        {
            Storage = new StorageConfig
            {
                StateDirectory = directory,
                LogDirectory = directory
            }
        };
        var game = config.GetOrCreateGame(gameId);

        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        using var coordinator = SessionCoordinator.Create(config, game, logging);

        try
        {
            await coordinator.RunAsync(CancellationToken.None);
            throw new InvalidOperationException("Expected missing executable failure.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("executable path", StringComparison.OrdinalIgnoreCase))
        {
        }

        Assert.Empty(Directory.GetFiles(directory, "*.session.json"));
    }

    [Fact]
    public async Task TestGameLockDisposeAfterAwait()
    {
        var directory = NewTempDir();
        using (var gameLock = GameLock.Acquire(directory, "demo"))
        {
            await Task.Delay(1);
        }

        using var reacquired = GameLock.Acquire(directory, "demo");
    }

    [Fact]
    public Task TestGameLockIdentity()
    {
        var directory = NewTempDir();
        using var slash = GameLock.Acquire(directory, "demo/game");
        using var backslash = GameLock.Acquire(directory, "demo\\game");

        try
        {
            using var duplicate = GameLock.Acquire(directory, "DEMO/GAME");
            throw new InvalidOperationException("Expected case-insensitive duplicate lock failure.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already wrapped", StringComparison.OrdinalIgnoreCase))
        {
        }

        return Task.CompletedTask;
    }
}
