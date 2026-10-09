using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

public sealed record CaptureGame(string GameTitle, string GameId, string GameIdProvider, string Platform)
{
    /// <summary>
    /// A game from the config as the capture API knows it: its API game id (the config's game id when there is none),
    /// provider, and its platform or the install's default.
    /// </summary>
    public static CaptureGame From(GameConfig game, ApiConfig api) => new(
        game.DisplayName,
        string.IsNullOrWhiteSpace(game.ApiGameId) ? game.GameId : game.ApiGameId,
        game.GameProvider,
        api.ResolvePlatform(game));
}

public sealed record RecordedSession(
    string GameTitle, string GameId, string GameIdProvider, string Platform, int DurationInSeconds);

/// <summary>A session that could not reach Playdex, kept in queue.jsonl until it can be sent.</summary>
public sealed record QueuedSession(
    string GameTitle, string GameId, string GameIdProvider, string Platform, int DurationInSeconds,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CaptureGame))]
[JsonSerializable(typeof(RecordedSession))]
[JsonSerializable(typeof(QueuedSession))]
public sealed partial class CaptureJson : JsonSerializerContext;

/// <summary>
/// Playdex capture API client (POST /capture/sessions/start, /ping, /end and /capture/sessions). The API key and URL
/// come from PLAYDEX_API_KEY and PLAYDEX_API_URL, or the install's config (<see cref="ConfigStore.Default"/>); the key
/// is never logged.
/// </summary>
public sealed class CaptureClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    /// <param name="handler">For tests; null sends real requests.</param>
    public CaptureClient(ApiConfig config, HttpMessageHandler? handler = null)
        : this(
            Environment.GetEnvironmentVariable("PLAYDEX_API_KEY") is { Length: > 0 } fromEnv ? fromEnv : config.ApiKey,
            Environment.GetEnvironmentVariable("PLAYDEX_API_URL") is { Length: > 0 } urlFromEnv ? urlFromEnv
                : string.IsNullOrWhiteSpace(config.BaseUrl) ? ApiConfig.DefaultBaseUrl : config.BaseUrl,
            handler ?? new HttpClientHandler(),
            TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds)))
    {
    }

    internal CaptureClient(string? apiKey, string baseUrl, HttpMessageHandler handler, TimeSpan? timeout = null)
    {
        _apiKey = apiKey?.Trim();
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = timeout ?? TimeSpan.FromSeconds(10),
        };
        Log.Info($"capture: api {_http.BaseAddress}, key {(IsConfigured ? "configured" : "missing")}");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public Task<bool> StartAsync(CaptureGame game) => PostAsync("capture/sessions/start", game, CaptureJson.Default.CaptureGame);

    public Task<bool> PingAsync(CaptureGame game) => PostAsync("capture/sessions/ping", game, CaptureJson.Default.CaptureGame);

    public Task<bool> EndAsync(CaptureGame game) => PostAsync("capture/sessions/end", game, CaptureJson.Default.CaptureGame);

    public Task<bool> RecordAsync(RecordedSession session) =>
        PostAsync("capture/sessions", session, CaptureJson.Default.RecordedSession);

    private async Task<bool> PostAsync<T>(string path, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        if (!IsConfigured)
        {
            return false;
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Content = JsonContent.Create(body, type);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request);
            Log.Info($"capture: {path} -> {(int)response.StatusCode} in {Metrics.MsSince(started):F0} ms");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Log.Info($"capture: {path} failed after {Metrics.MsSince(started):F0} ms: {ex.Message}");
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Offline queue. A session is either online (start succeeded; pings keep it alive and end closes it) or offline
/// (start failed): offline sessions are appended here whole and sent later with POST /capture/sessions.
/// </summary>
/// <remarks>
/// API gap: POST /capture/sessions sets startedAt = now - duration, so a queued session is stored with the wrong
/// start time. The fix is an optional startedAt on that endpoint (see spike-results.md), not a launcher workaround.
/// StartedAt is already kept in the queue file for when the API accepts it.
/// The launcher and the Wrapper share the file, so every change to it holds an exclusive lock on queue.jsonl.lock
/// (released by Windows if the process dies) as well as the in-process gate.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "It is a queue.")]
public sealed class SessionQueue(CaptureClient api, string? file = null) : IDisposable
{
    private static readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _file = file ?? Paths.QueueFile;

    public async Task EnqueueAsync(QueuedSession session)
    {
        await _gate.WaitAsync();
        try
        {
            // Losing the session is worse than racing another process's flush, so a lock timeout still appends.
            using var fileLock = await LockFileAsync();
            await File.AppendAllTextAsync(_file,JsonSerializer.Serialize(session, CaptureJson.Default.QueuedSession) + "\n");
            Log.Info($"queue: stored {session.GameTitle} ({session.DurationInSeconds}s) for later");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync()
    {
        if (!api.IsConfigured)
        {
            return;
        }
        await _gate.WaitAsync();
        try
        {
            using var fileLock = await LockFileAsync();
            if (fileLock is null || !File.Exists(_file))
            {
                return; // Another process is flushing.
            }
            var remaining = new List<string>();
            foreach (var line in await File.ReadAllLinesAsync(_file))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }
                QueuedSession? session;
                try
                {
                    session = JsonSerializer.Deserialize(line, CaptureJson.Default.QueuedSession);
                }
                catch (JsonException)
                {
                    Log.Info("queue: dropping unreadable line");
                    continue;
                }
                if (session is null)
                {
                    continue;
                }
                // Once one send fails, keep the rest without trying: the API is unreachable.
                var sent = remaining.Count == 0 && await api.RecordAsync(new RecordedSession(
                    session.GameTitle, session.GameId, session.GameIdProvider, session.Platform, session.DurationInSeconds));
                if (!sent)
                {
                    remaining.Add(line);
                }
            }
            if (remaining.Count == 0)
            {
                File.Delete(_file);
            }
            else
            {
                await File.WriteAllLinesAsync(_file, remaining);
            }
            Log.Info($"queue: flushed, {remaining.Count} left");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The cross-process lock, or null after <see cref="_lockTimeout"/>.</summary>
    private async Task<FileStream?> LockFileAsync()
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(_lockTimeout.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            try
            {
                return new FileStream(_file + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (Stopwatch.GetTimestamp() < deadline)
            {
                await Task.Delay(50);
            }
            catch (IOException ex)
            {
                Log.Error("queue: lock timed out", ex);
                return null;
            }
        }
    }

    public void Dispose() => _gate.Dispose();
}
