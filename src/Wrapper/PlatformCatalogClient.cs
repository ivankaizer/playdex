namespace Wrapper;

internal sealed record PlatformOption(string Key, string Name)
{
    public override string ToString() => Name;
}

internal sealed record PlatformCatalogLoad(
    IReadOnlyList<PlatformOption> Platforms,
    string? Error)
{
    public bool IsAvailable => Error is null;
}

internal sealed class PlatformCatalogClient : IDisposable
{
    private readonly HttpClient http;
    private readonly bool ownsClient;

    public PlatformCatalogClient(ApiConfig config, HttpMessageHandler? handler = null)
    {
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        ownsClient = true;
        http.BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/");
        http.Timeout = TimeSpan.FromSeconds(Math.Max(1, config.RequestTimeoutSeconds));
    }

    public async Task<PlatformCatalogLoad> LoadAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new PlatformCatalogLoad([], "Enter an API key to load Platforms.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "capture/platforms");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new PlatformCatalogLoad(
                    [],
                    $"Platform catalog returned status {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await JsonSerializer.DeserializeAsync<PlatformCatalogEnvelope>(
                stream,
                JsonOptions.Create(),
                cancellationToken);
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
        if (ownsClient)
        {
            http.Dispose();
        }
    }

    private sealed record PlatformCatalogEnvelope(IReadOnlyList<PlatformOption>? Data);
}
