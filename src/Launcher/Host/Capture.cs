using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Launcher.Host;

internal sealed record CaptureGame(string GameTitle, string GameId, string GameIdProvider, string Platform);

internal sealed record RecordedSession(
    string GameTitle, string GameId, string GameIdProvider, string Platform, int DurationInSeconds);

/// <summary>A session that could not reach Playdex, kept in queue.jsonl until it can be sent.</summary>
internal sealed record QueuedSession(
    string GameTitle, string GameId, string GameIdProvider, string Platform, int DurationInSeconds,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt);

internal sealed record LauncherConfig(string? ApiKey, string? ApiBaseUrl);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CaptureGame))]
[JsonSerializable(typeof(RecordedSession))]
[JsonSerializable(typeof(QueuedSession))]
[JsonSerializable(typeof(LauncherConfig))]
internal sealed partial class CaptureJson : JsonSerializerContext;

/// <summary>
/// Playdex capture API client (POST /capture/sessions/start, /ping, /end and /capture/sessions), ported from the
/// Wrapper's PingApiClient. The API key comes from PLAYDEX_API_KEY or config.json; it is never logged.
/// </summary>
internal sealed class CaptureClient : IDisposable
{
    private const string DefaultBaseUrl = "https://playdex.ejwan.dev/api";

    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public CaptureClient()
    {
        var config = ReadConfig();
        _apiKey = Environment.GetEnvironmentVariable("PLAYDEX_API_KEY") is { Length: > 0 } fromEnv ? fromEnv : config?.ApiKey;
        var baseUrl = Environment.GetEnvironmentVariable("PLAYDEX_API_URL") is { Length: > 0 } urlFromEnv
            ? urlFromEnv
            : config?.ApiBaseUrl ?? DefaultBaseUrl;
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(10),
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

    private static LauncherConfig? ReadConfig()
    {
        try
        {
            return File.Exists(Paths.ConfigFile)
                ? JsonSerializer.Deserialize(File.ReadAllText(Paths.ConfigFile), CaptureJson.Default.LauncherConfig)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Error("config.json unreadable", ex);
            return null;
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
/// </remarks>
internal sealed class SessionQueue(CaptureClient api) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task EnqueueAsync(QueuedSession session)
    {
        await _gate.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(Paths.QueueFile, JsonSerializer.Serialize(session, CaptureJson.Default.QueuedSession) + "\n");
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
            if (!File.Exists(Paths.QueueFile))
            {
                return;
            }
            var remaining = new List<string>();
            foreach (var line in await File.ReadAllLinesAsync(Paths.QueueFile))
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
                File.Delete(Paths.QueueFile);
            }
            else
            {
                await File.WriteAllLinesAsync(Paths.QueueFile, remaining);
            }
            Log.Info($"queue: flushed, {remaining.Count} left");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
