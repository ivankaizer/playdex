using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Api.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Api.Modules.Polling;

/// <summary>
/// Experimental Xbox presence provider backed by the unofficial OpenXBL API.
/// </summary>
public sealed class OpenXblClient(
    HttpClient httpClient,
    IOptions<OpenXblOptions> options) : IProviderClient, IProviderGameMetadataClient
{
    public string PlatformKey => "xbox";

    public async Task<ProviderPollResult?> GetCurrentlyPlaying(
        PlatformCredential credential,
        CancellationToken ct)
    {
        var apiKey = credential.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            throw new ProviderPollException("Xbox credential is missing an OpenXBL API key.");

        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var xuid = credential.AccountId?.Trim();
        var url = string.IsNullOrEmpty(xuid)
            ? $"{baseUrl}/v2/presence"
            : $"{baseUrl}/v2/presence/{Uri.EscapeDataString(xuid)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Authorization", apiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderPollException(
                "OpenXBL API rate limited.",
                ResolveRetryAfter(response) ?? TimeSpan.FromMinutes(1));
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderPollException("OpenXBL rejected the API key.");

        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderPollException(
                $"OpenXBL presence request returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        OpenXblEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<OpenXblEnvelope>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderPollException("OpenXBL presence response was invalid JSON.", innerException: ex);
        }

        if (envelope is null || envelope.Content.ValueKind != JsonValueKind.Object)
            throw new ProviderPollException("OpenXBL presence response was empty or invalid.");
        if (envelope.Code != 200)
            throw new ProviderPollException($"OpenXBL presence response reported code {envelope.Code}.");

        var presence = envelope.Content.Deserialize<OpenXblPresence>(JsonOptions);
        if (presence is null)
            throw new ProviderPollException("OpenXBL presence response did not contain presence data.");

        if (!string.Equals(presence.State, "Online", StringComparison.OrdinalIgnoreCase))
            return new ProviderPollResult(Game: null, RawPayload: rawJson);

        var title = (presence.Devices ?? [])
            .SelectMany(device => device.Titles ?? [])
            .Where(candidate =>
                !string.IsNullOrWhiteSpace(candidate.Name) &&
                string.Equals(candidate.State, "Active", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(candidate.Placement) ||
                 string.Equals(candidate.Placement, "Full", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(candidate =>
                string.Equals(candidate.Placement, "Full", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

        if (title is null)
            return new ProviderPollResult(Game: null, RawPayload: rawJson);

        return new ProviderPollResult(
            new CurrentlyPlayingResult(
                title.Name!.Trim(),
                string.IsNullOrWhiteSpace(title.Id) ? null : title.Id.Trim(),
                CapsuleUrl: null,
                Platform: PlatformKey),
            rawJson);
    }

    public async Task<string?> GetCapsuleUrl(
        PlatformCredential credential,
        string externalId,
        CancellationToken ct)
    {
        var apiKey = credential.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
            throw new ProviderPollException("Xbox credential is missing an OpenXBL API key.");

        var url = $"{options.Value.BaseUrl.TrimEnd('/')}/v2/marketplace/title/{Uri.EscapeDataString(externalId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-Authorization", apiKey);
        request.Headers.Accept.ParseAdd("application/json");

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderPollException(
                "OpenXBL marketplace API rate limited.",
                ResolveRetryAfter(response) ?? TimeSpan.FromMinutes(1));
        }
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ProviderPollException("OpenXBL rejected the API key.");
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        if (!response.IsSuccessStatusCode)
        {
            throw new ProviderPollException(
                $"OpenXBL marketplace request returned {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        var rawJson = await response.Content.ReadAsStringAsync(ct);
        OpenXblEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<OpenXblEnvelope>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new ProviderPollException("OpenXBL marketplace response was invalid JSON.", innerException: ex);
        }

        if (envelope is null || envelope.Content.ValueKind != JsonValueKind.Object)
            throw new ProviderPollException("OpenXBL marketplace response was empty or invalid.");
        if (envelope.Code != 200)
            throw new ProviderPollException($"OpenXBL marketplace response reported code {envelope.Code}.");

        var marketplace = envelope.Content.Deserialize<OpenXblMarketplaceContent>(JsonOptions);
        var poster = marketplace?.Products?
            .SelectMany(product => product.LocalizedProperties ?? [])
            .SelectMany(property => property.Images ?? [])
            .FirstOrDefault(image =>
                string.Equals(image.ImagePurpose, "Poster", StringComparison.OrdinalIgnoreCase));
        var posterUrl = NormalizeImageUrl(poster?.Uri);
        return posterUrl;
    }

    private static string? NormalizeImageUrl(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.StartsWith("//", StringComparison.Ordinal)) return $"https:{trimmed}";
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
               uri.Scheme is "http" or "https"
            ? uri.ToString()
            : null;
    }

    private static TimeSpan? ResolveRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
            return delta;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed record OpenXblEnvelope
    {
        [JsonPropertyName("content")]
        public JsonElement Content { get; init; }

        [JsonPropertyName("code")]
        public int Code { get; init; }
    }

    private sealed record OpenXblPresence
    {
        [JsonPropertyName("state")]
        public string? State { get; init; }

        [JsonPropertyName("devices")]
        public List<OpenXblDevice>? Devices { get; init; }
    }

    private sealed record OpenXblDevice
    {
        [JsonPropertyName("titles")]
        public List<OpenXblTitle>? Titles { get; init; }
    }

    private sealed record OpenXblTitle
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("placement")]
        public string? Placement { get; init; }

        [JsonPropertyName("state")]
        public string? State { get; init; }
    }

    private sealed record OpenXblMarketplaceContent
    {
        [JsonPropertyName("Products")]
        public List<OpenXblProduct>? Products { get; init; }
    }

    private sealed record OpenXblProduct
    {
        [JsonPropertyName("LocalizedProperties")]
        public List<OpenXblLocalizedProperty>? LocalizedProperties { get; init; }
    }

    private sealed record OpenXblLocalizedProperty
    {
        [JsonPropertyName("Images")]
        public List<OpenXblImage>? Images { get; init; }
    }

    private sealed record OpenXblImage
    {
        [JsonPropertyName("ImagePurpose")]
        public string? ImagePurpose { get; init; }

        [JsonPropertyName("Uri")]
        public string? Uri { get; init; }
    }
}
