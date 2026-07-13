using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Playdex.Web.Auth;
using Playdex.Web.Models;

namespace Playdex.Web.Api;

public sealed class PlaydexApiClient(HttpClient httpClient, AuthSession auth, string apiBase)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _apiBase = apiBase.Trim().TrimEnd('/');

    public async Task<T> SendAsync<T>(
        string path,
        HttpMethod? method = null,
        object? body = null,
        bool authenticated = true,
        Func<Task>? onAuthFailure = null,
        CancellationToken cancellationToken = default)
    {
        var firstResponse = await SendOnceAsync<T>(
            path, method ?? HttpMethod.Get, body, authenticated, cancellationToken);
        if (firstResponse.Error?.StatusCode != HttpStatusCode.Unauthorized || !authenticated)
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
            path, method ?? HttpMethod.Get, body, authenticated: true, cancellationToken);
        if (retryResponse.Error is not null) throw retryResponse.Error;
        return retryResponse.Value!;
    }

    private async Task<Response<T>> SendOnceAsync<T>(
        string path,
        HttpMethod method,
        object? body,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, BuildUrl(path));
        if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);

        if (authenticated)
        {
            var accessToken = await auth.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(accessToken))
            {
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            }
        }

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new Response<T>(default, await CreateExceptionAsync(response, cancellationToken));
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new Response<T>(
            await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken),
            null);
    }

    private async Task<bool> TryRefreshAsync(CancellationToken cancellationToken)
    {
        var refreshToken = await auth.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refreshToken)) return false;

        try
        {
            var response = await SendOnceAsync<RefreshResponse>(
                "/refresh",
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

    private static async Task<ApiException> CreateExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var detail = "Request failed.";
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
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

    private sealed record Response<T>(T? Value, ApiException? Error);
}
