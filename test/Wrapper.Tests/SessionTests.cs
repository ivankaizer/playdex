using System.Net;
using System.Text.Json;
using Wrapper;
using static Wrapper.Tests.TestPaths;

namespace Wrapper.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task TestPingContract()
    {
        var directory = NewTempDir();
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
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
            Platform = "gog",
            ApiKey = "game-key"
        };
        using var api = new PingApiClient(config, game, logging.CreateLogger<PingApiClient>(), handler);

        Assert.True(await api.PingAsync(CancellationToken.None));
        var request = handler.Requests.Single();
        Assert.Equal("POST", request.Method);
        Assert.Equal("https://example.test/api/capture/sessions/ping", request.Uri);
        Assert.Equal("Bearer game-key", request.Authorization);

        using var json = JsonDocument.Parse(request.Body);
        Assert.Equal("Demo Game", json.RootElement.GetProperty("gameTitle").GetString());
        Assert.Equal("1234", json.RootElement.GetProperty("gameId").GetString());
        Assert.Equal("steam", json.RootElement.GetProperty("gameIdProvider").GetString());
        Assert.Equal("gog", json.RootElement.GetProperty("platform").GetString());
        Assert.True(!json.RootElement.TryGetProperty("gameProvider", out _));
    }

    [Fact]
    public async Task TestPingUsesGlobalPlatformDefault()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var logging = AppLoggerFactory.CreateInDirectory(NewTempDir());
        using var api = new PingApiClient(
            new ApiConfig { BaseUrl = "https://example.test/api", ApiKey = "key", Platform = "steam" },
            new GameConfig { GameId = "demo", DisplayName = "Demo", Platform = null },
            logging.CreateLogger<PingApiClient>(),
            handler);

        Assert.True(await api.PingAsync(CancellationToken.None));
        using var json = JsonDocument.Parse(handler.Requests.Single().Body);
        Assert.Equal("steam", json.RootElement.GetProperty("platform").GetString());
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
    public async Task TestPingFailureDiscarded()
    {
        var directory = NewTempDir();
        var handler = new RecordingHandler(HttpStatusCode.ServiceUnavailable);
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        using var api = new PingApiClient(
            new ApiConfig { BaseUrl = "https://example.test/api", ApiKey = "key" },
            new GameConfig { GameId = "demo", DisplayName = "Demo" },
            logging.CreateLogger<PingApiClient>(),
            handler);

        Assert.True(!await api.PingAsync(CancellationToken.None));
        Assert.True(!await api.PingAsync(CancellationToken.None));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Empty(Directory.GetFiles(directory, "*.events.json"));
    }

    [Fact]
    public async Task TestPingCadence()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var logging = AppLoggerFactory.CreateInDirectory(NewTempDir());
        using var api = new PingApiClient(
            new ApiConfig { BaseUrl = "https://example.test/api", ApiKey = "key" },
            new GameConfig { GameId = "demo", DisplayName = "Demo" },
            logging.CreateLogger<PingApiClient>(),
            handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(130));

        await new PingService(api, TimeSpan.FromMilliseconds(25)).RunAsync(cancellation.Token);

        Assert.True(handler.Requests.Count >= 3);
    }

    [Fact]
    public async Task TestApiDisabledSession()
    {
        var directory = NewTempDir();
        using var logging = AppLoggerFactory.CreateInDirectory(directory);
        var config = new WrapperConfig
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
    public async Task TestSessionCoordinatorPingOutage()
    {
        var directory = NewTempDir();
        var gameId = $"demo-{Guid.NewGuid():N}";
        var config = new WrapperConfig
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
        using var coordinator = SessionCoordinator.Create(config, game, logging);

        await coordinator.RunAsync(CancellationToken.None);

        Assert.Empty(Directory.GetFiles(directory, "*.events.json"));
        Assert.Empty(Directory.GetFiles(directory, "*.session.json"));
    }

    [Fact]
    public async Task TestMissingExecutableDoesNotCreateSession()
    {
        var directory = NewTempDir();
        var gameId = $"demo-{Guid.NewGuid():N}";
        var config = new WrapperConfig
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
