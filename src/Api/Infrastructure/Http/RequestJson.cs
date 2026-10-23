namespace Api.Infrastructure.Http;

public static class RequestJson
{
    /// <summary>
    /// <see cref="HttpRequestJsonExtensions.ReadFromJsonAsync{TValue}(HttpRequest, CancellationToken)"/> throws an
    /// <see cref="InvalidOperationException"/>, reported as a 500, when the body is not JSON. This answers 415.
    /// Malformed JSON still throws a <see cref="System.Text.Json.JsonException"/>, which becomes a 400.
    /// </summary>
    public static ValueTask<T?> ReadJsonBodyAsync<T>(this HttpRequest request, CancellationToken ct = default)
    {
        if (!request.HasJsonContentType())
        {
            throw new BadHttpRequestException(
                "The request body is not JSON.",
                StatusCodes.Status415UnsupportedMediaType);
        }

        return request.ReadFromJsonAsync<T>(ct);
    }
}
