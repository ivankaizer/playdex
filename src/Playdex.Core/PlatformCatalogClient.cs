using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Playdex.Core;

public sealed record PlatformOption(string Key, string Name)
{
    public override string ToString() => Name;
}

public sealed record PlatformCatalogLoad(
    IReadOnlyList<PlatformOption> Platforms,
    string? Error)
{
    public bool IsAvailable => Error is null;
}

public sealed class PlatformCatalogClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public PlatformCatalogClient(ApiConfig config, HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _ownsClient = true;
        _http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds));
    }

    public async Task<PlatformCatalogLoad> LoadAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new PlatformCatalogLoad([], "Enter an API key to load Platforms.");
        }

        if (!ApiConfig.IsSecure(_http.BaseAddress!))
        {
            return new PlatformCatalogLoad([], "The API URL must use HTTPS (HTTP only for localhost).");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "capture/platforms");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PlatformCatalogLoad(
                    [],
                    $"Platform catalog returned status {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await JsonSerializer.DeserializeAsync(stream, PlatformCatalogJson.Default.PlatformCatalogEnvelope, cancellationToken);
            if (envelope?.Data is null)
            {
                return new PlatformCatalogLoad([], "Platform catalog returned an invalid response.");
            }

            return new PlatformCatalogLoad(envelope.Data, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new PlatformCatalogLoad([], "Platform catalog is unavailable.");
        }
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }

}

internal sealed record PlatformCatalogEnvelope(IReadOnlyList<PlatformOption>? Data);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PlatformCatalogEnvelope))]
internal sealed partial class PlatformCatalogJson : JsonSerializerContext;
