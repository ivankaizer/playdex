using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Api.Modules.Posters;

public sealed partial class PosterService(
    IPosterStore store,
    IHttpClientFactory httpClientFactory,
    IOptions<PostersOptions> options,
    ILogger<PosterService> logger)
{
    public const string WeModHttpClient = "wemod";

    // Concurrent misses for the same poster share one WeMod download.
    private readonly ConcurrentDictionary<string, Lazy<Task<StoredPoster?>>> _inFlight = new();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Storing poster {Key} in R2 failed")]
    private partial void LogStoreFailed(Exception exception, string key);

    public async Task<StoredPoster?> GetSteamCapsule(long appId, CancellationToken ct)
    {
        var key = $"steam/{appId}/hero_capsule.webp";
        var stored = await store.Find(key, ct);
        if (stored is not null)
        {
            return stored;
        }

        var download = _inFlight.GetOrAdd(key, _ => new Lazy<Task<StoredPoster?>>(() => DownloadAndStore(key, appId)));
        try
        {
            return await download.Value.WaitAsync(ct);
        }
        finally
        {
            _inFlight.TryRemove(new KeyValuePair<string, Lazy<Task<StoredPoster?>>>(key, download));
        }
    }

    private async Task<StoredPoster?> DownloadAndStore(string key, long appId)
    {
        // Not tied to one request: other callers may be waiting on this download.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = httpClientFactory.CreateClient(WeModHttpClient);
        var url = PosterUrls.WeModSteamCapsule(options.Value.WeModBaseUrl, appId.ToString(CultureInfo.InvariantCulture));
        using var response = await client.GetAsync(url, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var poster = new StoredPoster(
            await response.Content.ReadAsByteArrayAsync(timeout.Token),
            response.Content.Headers.ContentType?.MediaType ?? "image/webp");
        try
        {
            await store.Save(key, poster, timeout.Token);
        }
        catch (Exception ex)
        {
            LogStoreFailed(ex, key);
        }

        return poster;
    }
}
