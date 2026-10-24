using System.Diagnostics;
using System.Net.Http.Headers;
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

/// <param name="StartedAt">When it started, for a session sent after it ended (the offline queue); null: it just ended.</param>
public sealed record RecordedSession(
    string GameTitle, string GameId, string GameIdProvider, string Platform, int DurationInSeconds, DateTimeOffset? StartedAt = null);

/// <summary>A session that could not reach Playdex, kept in queue.jsonl until it can be sent.</summary>
public sealed record QueuedSession(
    string GameTitle, string GameId, string GameIdProvider, string Platform, int DurationInSeconds,
    DateTimeOffset StartedAt, DateTimeOffset EndedAt);

/// <summary>
/// A queued session Playdex refused for good, kept in queue.rejected.jsonl so the rest of the queue goes on: the session
/// (or the <paramref name="Line"/> that could not be read as one) and why.
/// </summary>
/// <param name="Status">The HTTP status of the refusal; null for a line that could not be read.</param>
public sealed record RejectedSession(DateTimeOffset RejectedAt, int? Status, string Reason, QueuedSession? Session, string? Line = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(CaptureGame))]
[JsonSerializable(typeof(RecordedSession))]
[JsonSerializable(typeof(QueuedSession))]
[JsonSerializable(typeof(RejectedSession))]
public sealed partial class CaptureJson : JsonSerializerContext;

public enum DeliveryStatus
{
    /// <summary>Playdex took it.</summary>
    Sent,

    /// <summary>
    /// Try again later: no answer (offline, a timeout), a server error, 429 or 408, and the answers that are about the
    /// API key or URL rather than the session (401, 403, 404, 405). No key configured is also this.
    /// </summary>
    Unavailable,

    /// <summary>Any other 4xx: Playdex will never take this session as it is (a bad game id, an unknown platform).</summary>
    Rejected,
}

/// <param name="Status">The HTTP status; null without an answer.</param>
/// <param name="Reason">Why it was not sent: the server's error message, "HTTP 503", or the connection error.</param>
public sealed record Delivery(DeliveryStatus Outcome, int? Status = null, string? Reason = null)
{
    public static Delivery Sent { get; } = new(DeliveryStatus.Sent);

    /// <summary>What an HTTP answer means for a sent session; <paramref name="body"/> gives a refusal its reason.</summary>
    public static Delivery FromResponse(int status, string? body)
    {
        if (status is >= 200 and < 300)
        {
            return Sent;
        }
        var reason = ErrorMessage(body) is { } message ? $"HTTP {status}: {message}" : $"HTTP {status}";
        var retry = status is < 400 or >= 500 or 401 or 403 or 404 or 405 or 408 or 429;
        return new Delivery(retry ? DeliveryStatus.Unavailable : DeliveryStatus.Rejected, status, reason);
    }

    /// <summary>
    /// The message of a Playdex error ({"error":{"error":"…"}}) or a problem ({"detail":"…"}), else the body itself
    /// (shortened); null for an empty body.
    /// </summary>
    internal static string? ErrorMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
                    && error.TryGetProperty("error", out var text) && text.ValueKind == JsonValueKind.String)
                {
                    return text.GetString();
                }
                if (root.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                {
                    return detail.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }
        var trimmed = body.Trim();
        return trimmed.Length <= 300 ? trimmed : trimmed[..300] + "…";
    }
}

public enum ApiKeyStatus
{
    /// <summary>The server answered with the user's data.</summary>
    Accepted,

    /// <summary>The server refused the key (401 or 403).</summary>
    Rejected,

    /// <summary>No answer, or one that says nothing about the key (a wrong URL, a server error).</summary>
    Unreachable,
}

/// <param name="Detail">Why the key couldn't be checked: "HTTP 502", or the connection error.</param>
public sealed record ApiKeyCheck(ApiKeyStatus Status, string? Detail = null);

/// <summary>
/// Playdex API client for what the API key reaches: capture (POST /capture/sessions/start, /ping, /end and
/// /capture/sessions) and the launcher's read endpoint (GET /launcher/summary). The API key and URL
/// come from PLAYDEX_API_KEY and PLAYDEX_API_URL, or the install's config (<see cref="ConfigStore.Default"/>); the key
/// is never logged, and is sent only over HTTPS (or HTTP to localhost).
/// </summary>
public sealed class CaptureClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly Func<string> _baseUrl;

    /// <summary>
    /// A client that reads <paramref name="config"/> on every request, so a changed API key or URL applies at once.
    /// PLAYDEX_API_KEY and PLAYDEX_API_URL override it; a blank URL means <see cref="ApiConfig.DefaultBaseUrl"/>.
    /// </summary>
    /// <param name="handler">For tests; null sends real requests.</param>
    public CaptureClient(ApiConfig config, HttpMessageHandler? handler = null)
        : this(
            () => Environment.GetEnvironmentVariable("PLAYDEX_API_KEY") is { Length: > 0 } fromEnv ? fromEnv : config.ApiKey,
            () => Environment.GetEnvironmentVariable("PLAYDEX_API_URL") is { Length: > 0 } urlFromEnv ? urlFromEnv
                : string.IsNullOrWhiteSpace(config.BaseUrl) ? ApiConfig.DefaultBaseUrl : config.BaseUrl,
            handler ?? new HttpClientHandler(),
            TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds)))
    {
    }

    internal CaptureClient(string? apiKey, string baseUrl, HttpMessageHandler handler, TimeSpan? timeout = null)
        : this(() => apiKey, () => baseUrl, handler, timeout ?? TimeSpan.FromSeconds(10))
    {
    }

    private CaptureClient(Func<string?> apiKey, Func<string> baseUrl, HttpMessageHandler handler, TimeSpan timeout)
    {
        _apiKey = apiKey;
        _baseUrl = baseUrl;
        _http = new HttpClient(handler) { Timeout = timeout };
        Log.Info($"capture: api {_baseUrl()}, key {(IsConfigured ? "configured" : "missing")}");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey());

    /// <summary>
    /// The API's address. A config saved before HTTP was refused, or PLAYDEX_API_URL, can still name an HTTP server on
    /// the network: requests to it fail without sending the key (logged, and a finished session stays queued).
    /// </summary>
    private Uri BaseAddress
    {
        get
        {
            var address = new Uri(_baseUrl().Trim().TrimEnd('/') + "/");
            return ApiConfig.IsSecure(address)
                ? address
                : throw new HttpRequestException($"the API URL {address} must use HTTPS (HTTP only for localhost); the key was not sent");
        }
    }

    public Task<bool> StartAsync(CaptureGame game) => PostAsync("capture/sessions/start", game, CaptureJson.Default.CaptureGame);

    public Task<bool> PingAsync(CaptureGame game) => PostAsync("capture/sessions/ping", game, CaptureJson.Default.CaptureGame);

    public Task<bool> EndAsync(CaptureGame game) => PostAsync("capture/sessions/end", game, CaptureJson.Default.CaptureGame);

    /// <summary>Sends a finished session (POST /capture/sessions): sent, to try again later, or refused for good.</summary>
    public async Task<Delivery> RecordAsync(RecordedSession session)
    {
        const string path = "capture/sessions";
        if (!IsConfigured)
        {
            return new Delivery(DeliveryStatus.Unavailable, Reason: "no API key");
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = Post(path, session, CaptureJson.Default.RecordedSession);
            using var response = await _http.SendAsync(request);
            var status = (int)response.StatusCode;
            Log.Info($"capture: {path} -> {status} in {Metrics.MsSince(started):F0} ms");
            return Delivery.FromResponse(status, response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Log.Info($"capture: {path} failed after {Metrics.MsSince(started):F0} ms: {ex.Message}");
            return new Delivery(DeliveryStatus.Unavailable, Reason: ex is TaskCanceledException ? "no answer in time" : ex.Message);
        }
    }

    /// <summary>
    /// The user's Playdex data for the launcher (GET launcher/summary, API key only), in this PC's time zone: the
    /// response's "data" as raw JSON, or null without a key, offline, or on an error.
    /// </summary>
    public Task<string?> GetLauncherSummaryAsync(CancellationToken ct = default) => GetDataAsync(SummaryPath(), "launcher/summary", ct);

    /// <summary>
    /// The web's recap of one calendar year (GET launcher/recap?year=&amp;tz=, API key only), as raw JSON, or null without
    /// a key, offline, or on an error. Its days and hours are those of <paramref name="timeZone"/>, the page's IANA
    /// zone, or of this PC's zone when the page sent none.
    /// </summary>
    public Task<string?> GetLauncherRecapAsync(int year, string? timeZone = null, CancellationToken ct = default) =>
        GetDataAsync(RecapPath(year, timeZone), "launcher/recap", ct);

    internal static string RecapPath(int year, string? timeZone)
    {
        var zone = string.IsNullOrWhiteSpace(timeZone) ? TimeZoneInfo.Local.Id : timeZone.Trim();
        return $"launcher/recap?year={year.ToString(System.Globalization.CultureInfo.InvariantCulture)}&tz={Uri.EscapeDataString(zone)}";
    }

    private async Task<string?> GetDataAsync(string path, string name, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return null;
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, path));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey()!.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, ct);
            Log.Info($"playdex: {name} -> {(int)response.StatusCode} in {Metrics.MsSince(started):F0} ms");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }
            using var body = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return body.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
                ? data.GetRawText()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or JsonException)
        {
            Log.Info($"playdex: {name} failed after {Metrics.MsSince(started):F0} ms: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Whether the server takes the API key, asked the way the launcher reads its data (GET launcher/summary). The
    /// installer checks a key before it saves it.
    /// </summary>
    public async Task<ApiKeyCheck> CheckKeyAsync(CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            return new ApiKeyCheck(ApiKeyStatus.Rejected);
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, SummaryPath()));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey()!.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var status = (int)response.StatusCode;
            Log.Info($"playdex: key check -> {status} in {Metrics.MsSince(started):F0} ms");
            return response.IsSuccessStatusCode ? new ApiKeyCheck(ApiKeyStatus.Accepted)
                : status is 401 or 403 ? new ApiKeyCheck(ApiKeyStatus.Rejected)
                : new ApiKeyCheck(ApiKeyStatus.Unreachable, $"HTTP {status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Log.Info($"playdex: key check failed after {Metrics.MsSince(started):F0} ms: {ex.Message}");
            return new ApiKeyCheck(ApiKeyStatus.Unreachable, ex is TaskCanceledException ? "no answer in time" : ex.Message);
        }
    }

    /// <summary>launcher/summary in this PC's time zone (Windows id, with the UTC offset as a fallback).</summary>
    private static string SummaryPath()
    {
        var zone = TimeZoneInfo.Local;
        var offset = (int)zone.GetUtcOffset(DateTimeOffset.UtcNow).TotalMinutes;
        return $"launcher/summary?tz={Uri.EscapeDataString(zone.Id)}&utcOffsetMinutes={offset.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private async Task<bool> PostAsync<T>(string path, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        if (!IsConfigured)
        {
            return false;
        }
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var request = Post(path, body, type);
            using var response = await _http.SendAsync(request);
            Log.Info($"capture: {path} -> {(int)response.StatusCode} in {Metrics.MsSince(started):F0} ms");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            Log.Info($"capture: {path} failed after {Metrics.MsSince(started):F0} ms: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// A POST with the JSON body serialized up front, so it carries a Content-Length: JsonContent streams it chunked,
    /// which Playdex APIs from before the chunked-body fix read as no body at all (self-hosted servers update separately).
    /// </summary>
    private HttpRequestMessage Post<T>(string path, T body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, type));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress, path)) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey()!.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>
/// Offline queue. A session is either online (start succeeded; pings keep it alive and end closes it) or offline
/// (start failed): offline sessions are appended here whole and sent later with POST /capture/sessions, with their
/// real start time. A session still running is kept in <see cref="Journal"/> (queue.running next to queue.jsonl), so
/// one cut short by a crash or a shutdown comes back here at the next flush instead of being lost. A flush stops at the
/// first session Playdex can't take now (offline, 5xx, 429) and keeps it and the rest; one it refuses for good (a 4xx
/// about the session) goes to <see cref="RejectedFile"/> with the reason, and the flush goes on.
/// </summary>
/// <remarks>
/// The app and the per-game launchers share the file, so every change to it holds an exclusive lock on
/// queue.jsonl.lock (released by Windows if the process dies) as well as the in-process gate.
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711", Justification = "It is a queue.")]
public sealed class SessionQueue(CaptureClient api, string? file = null) : IDisposable
{
    private static readonly TimeSpan _lockTimeout = TimeSpan.FromMinutes(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _file = file ?? Paths.QueueFile;

    /// <summary>
    /// The sessions Playdex refused for good (a 4xx that isn't about the key or the URL), one <see cref="RejectedSession"/>
    /// per line next to the queue (queue.rejected.jsonl), so they don't hold up the rest. Nothing sends them again.
    /// </summary>
    public string RejectedFile => Path.ChangeExtension(_file, ".rejected.jsonl");

    /// <summary>The sessions running now, for <see cref="SessionRecorder"/>; recovered into the queue by <see cref="FlushAsync"/>.</summary>
    internal SessionJournal Journal { get; } = new(Path.ChangeExtension(file ?? Paths.QueueFile, ".running"));

    public async Task EnqueueAsync(QueuedSession session)
    {
        await _gate.WaitAsync();
        try
        {
            // Losing the session is worse than racing another process's flush, so a lock timeout still appends.
            using var fileLock = await LockFileAsync();
            await File.AppendAllTextAsync(_file, JsonSerializer.Serialize(session, CaptureJson.Default.QueuedSession) + "\n");
            Log.Info($"queue: stored {session.GameTitle} ({session.DurationInSeconds}s) for later");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Queues the sessions that processes which have ended left running (<see cref="SessionJournal.Recover"/>), then
    /// sends every queued session while Playdex takes them.
    /// </summary>
    public async Task FlushAsync()
    {
        await _gate.WaitAsync();
        try
        {
            using var fileLock = await LockFileAsync();
            if (fileLock is null)
            {
                return; // Another process is flushing.
            }
            RecoverRunningSessions();
            if (!api.IsConfigured || !File.Exists(_file))
            {
                return;
            }
            var remaining = new List<string>();
            var unavailable = false;
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
                    session = null;
                }
                if (session is null)
                {
                    if (!SetAside(new RejectedSession(DateTimeOffset.Now, null, "not a queued session", null, line)))
                    {
                        remaining.Add(line);
                    }
                    continue;
                }
                // Once Playdex can't be reached, keep the rest without trying.
                if (unavailable)
                {
                    remaining.Add(line);
                    continue;
                }
                var delivery = await api.RecordAsync(new RecordedSession(
                    session.GameTitle, session.GameId, session.GameIdProvider, session.Platform, session.DurationInSeconds, session.StartedAt));
                switch (delivery.Outcome)
                {
                    case DeliveryStatus.Unavailable:
                        unavailable = true;
                        remaining.Add(line);
                        break;
                    case DeliveryStatus.Rejected:
                        // Refused for good: set aside so the sessions behind it still go, never retried as it is.
                        if (!SetAside(new RejectedSession(DateTimeOffset.Now, delivery.Status, delivery.Reason ?? "rejected", session)))
                        {
                            remaining.Add(line);
                        }
                        break;
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

    /// <summary>
    /// Under the file lock: a session Playdex refused, or a line that isn't one, goes to <see cref="RejectedFile"/> with
    /// the reason, and the log says so. False when it could not be written; it then stays in the queue.
    /// </summary>
    private bool SetAside(RejectedSession rejected)
    {
        var what = rejected.Session is { } session
            ? $"{session.GameTitle} ({session.DurationInSeconds}s from {session.StartedAt:O})"
            : "an unreadable line";
        try
        {
            File.AppendAllText(RejectedFile, JsonSerializer.Serialize(rejected, CaptureJson.Default.RejectedSession) + "\n");
            Log.Info($"ERROR queue: Playdex will not take {what}: {rejected.Reason}; moved to {Path.GetFileName(RejectedFile)}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error($"queue: could not set aside {what} ({rejected.Reason}); it stays queued", ex);
            return false;
        }
    }

    /// <summary>Under the file lock: the sessions left running by ended processes go to the end of the queue.</summary>
    private void RecoverRunningSessions()
    {
        try
        {
            Journal.Recover(session =>
                File.AppendAllText(_file, JsonSerializer.Serialize(session, CaptureJson.Default.QueuedSession) + "\n"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The session that failed to append stays in the journal for the next flush.
            Log.Error("queue: could not recover the sessions left running", ex);
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
