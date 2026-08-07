namespace GameSessionWrapper;

internal sealed record GamePing(string GameTitle, string GameId, string GameProvider);

internal sealed partial class PingApiClient : IDisposable
{
    private readonly ApiConfig _config;
    private readonly GameConfig _game;
    private readonly ILogger<PingApiClient> _logger;
    private readonly HttpClient _http;
    private const string _pingPath = "playnite/sessions/ping";

    public PingApiClient(ApiConfig config, GameConfig game, ILogger<PingApiClient> logger, HttpMessageHandler? handler = null)
    {
        _config = config;
        _game = game;
        _logger = logger;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds));
    }

    public async Task<bool> PingAsync(CancellationToken cancellationToken)
    {
        var apiGameId = string.IsNullOrWhiteSpace(_game.ApiGameId) ? _game.GameId : _game.ApiGameId;
        var ping = new GamePing(_game.DisplayName, apiGameId, _game.GameProvider);
        var apiKey = _config.ResolveApiKey(_game);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            LogMissingApiKey(_logger, _game.GameId);
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _pingPath);
            request.Content = JsonContent.Create(ping, options: JsonOptions.Create());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                LogUnsuccessfulStatus(_logger, _game.GameId, (int)response.StatusCode);
                return false;
            }

            LogPingSent(_logger, _game.GameId);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            LogPingFailed(_logger, _game.GameId, ex);
            return false;
        }
    }

    public void Dispose() => _http.Dispose();

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Skipping API ping for {GameId}: missing API key")]
    private static partial void LogMissingApiKey(ILogger logger, string gameId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "API ping for {GameId} returned status {StatusCode}")]
    private static partial void LogUnsuccessfulStatus(ILogger logger, string gameId, int statusCode);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "API ping sent for {GameId}")]
    private static partial void LogPingSent(ILogger logger, string gameId);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "API ping failed for {GameId}")]
    private static partial void LogPingFailed(ILogger logger, string gameId, Exception exception);
}

internal sealed class PingService(PingApiClient api, TimeSpan interval)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await api.PingAsync(cancellationToken);
                await Task.Delay(interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
