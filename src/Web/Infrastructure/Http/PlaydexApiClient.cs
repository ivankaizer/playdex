using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Web.Infrastructure.Auth;
using Web.Observability;

namespace Web.Infrastructure.Http;

public sealed class PlaydexApiClient(HttpClient httpClient, AuthSession auth, string apiBase)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _apiBase = apiBase.Trim().TrimEnd('/');
    private readonly Uri? _apiOrigin = ResolveAbsoluteUrl(apiBase.Trim(), httpClient.BaseAddress);

    public async Task<T> SendAsync<T>(
        string path,
        HttpMethod? method = null,
        object? body = null,
        bool authenticated = true,
        Func<Task>? onAuthFailure = null,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(path);
        var useAuthentication = authenticated && IsApiOrigin(url);
        var firstResponse = await SendOnceAsync<T>(
            url, method ?? HttpMethod.Get, body, useAuthentication, cancellationToken);
        if (firstResponse.Error?.StatusCode != HttpStatusCode.Unauthorized || !useAuthentication)
        {
            if (firstResponse.Error is not null) throw firstResponse.Error;
            return firstResponse.Value!;
        }

        var originalUnauthorized = firstResponse.Error;
        if (!await TryRefreshAsync(cancellationToken))
        {
            if (onAuthFailure is not null) await onAuthFailure();
            throw originalUnauthorized;
        }

        var retryResponse = await SendOnceAsync<T>(
            url, method ?? HttpMethod.Get, body, authenticated: true, cancellationToken);
        if (retryResponse.Error is not null) throw retryResponse.Error;
        return retryResponse.Value!;
    }

    private async Task<Response<T>> SendOnceAsync<T>(
        string url,
        HttpMethod method,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        var transaction = SentryApiTracing.Start(method, url);
        using var request = new HttpRequestMessage(method, url);
        SentryApiTracing.AddTraceHeader(request, transaction);
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

        if (authenticated)
        {
            var accessToken = await auth.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request, cancellationToken);
            transaction?.Finish(SentryApiTracing.ToSpanStatus(response.StatusCode));
        }
        catch (Exception exception)
        {
            transaction?.Finish(exception);
            throw;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return new Response<T>(default, await CreateExceptionAsync(response, cancellationToken));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var root = await JsonSerializer.DeserializeAsync<JsonElement>(stream, JsonOptions, cancellationToken);
            var payload = root;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("data", out var data) &&
                root.TryGetProperty("error", out _))
            {
                payload = data;
            }

            var value = payload.Deserialize<T>(JsonOptions);
            if (value is null) throw new JsonException("Successful response contained no JSON data.");
            return new Response<T>(value, null);
        }
    }

    private async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await auth.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refreshToken)) return false;

        try
        {
            var response = await SendOnceAsync<RefreshResponse>(
                BuildUrl("/refresh"),
                HttpMethod.Post,
                new RefreshRequest(refreshToken),
                authenticated: false,
                cancellationToken);
            if (response.Error is not null ||
                string.IsNullOrEmpty(response.Value?.AccessToken) ||
                string.IsNullOrEmpty(response.Value.RefreshToken))
            {
                return false;
            }

            await auth.StoreTokensAsync(response.Value.AccessToken, response.Value.RefreshToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private string BuildUrl(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return path;
        }

        return $"{_apiBase}/{path.TrimStart('/')}";
    }

    private bool IsApiOrigin(string url)
    {
        var target = ResolveAbsoluteUrl(url, httpClient.BaseAddress);
        return target is not null &&
            _apiOrigin is not null &&
            target.Scheme.Equals(_apiOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
            target.IdnHost.Equals(_apiOrigin.IdnHost, StringComparison.OrdinalIgnoreCase) &&
            target.Port == _apiOrigin.Port;
    }

    private static Uri? ResolveAbsoluteUrl(string url, Uri? baseAddress)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute;
        }

        return baseAddress is not null && Uri.TryCreate(baseAddress, url, out var resolved)
            ? resolved
            : null;
    }

    private static async Task<ApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var detail = "Request failed.";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (TryGetErrorDetail(document.RootElement, out var envelopeDetail))
            {
                detail = envelopeDetail;
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object &&
                     document.RootElement.TryGetProperty("detail", out var property) &&
                     property.ValueKind == JsonValueKind.String &&
                     !string.IsNullOrWhiteSpace(property.GetString()))
            {
                detail = property.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        return new ApiException(response.StatusCode, detail);
    }

    private static bool TryGetErrorDetail(JsonElement root, out string detail)
    {
        detail = "";
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("error", out var error) ||
            error.ValueKind != JsonValueKind.Object ||
            !error.TryGetProperty("error", out var message) ||
            message.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(message.GetString()))
        {
            return false;
        }

        detail = message.GetString()!;
        return true;
    }

    private sealed record Response<T>(T? Value, ApiException? Error);
}
