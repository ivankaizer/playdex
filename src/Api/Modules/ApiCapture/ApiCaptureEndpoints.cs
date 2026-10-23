using System.Text.Json;
using Api.Infrastructure.Common;
using Api.Infrastructure.Http;

namespace Api.Modules.ApiCapture;

public static class ApiCaptureEndpoints
{
    private static readonly HashSet<string> AllowedIdentityProviders = ["steam", "psn", "xbox", "igdb"];
    // A capture body is a few short fields; anything bigger is a mistake or abuse.
    private const long MaxBodyBytes = 16 * 1024;
    private const int MaxTitleLength = 200;
    private const int MaxGameIdLength = 64;

    public static void MapApiCaptureEndpoints(this RouteGroupBuilder api)
    {
        var capture = api.MapGroup("/capture").RequireRateLimiting(RateLimiting.Capture);
        MapPlatformEndpoints(capture);
        MapSessionEndpoints(capture);
    }

    private static void MapPlatformEndpoints(RouteGroupBuilder capture)
    {
        capture.MapGet("/platforms", async (HttpContext http) =>
        {
            var service = http.RequestServices.GetRequiredService<ApiCaptureService>();
            var result = await service.ListPlatforms(ExtractApiKey(http), http.RequestAborted);
            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });
    }

    private static void MapSessionEndpoints(RouteGroupBuilder capture)
    {

        capture.MapPost("/sessions", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<ApiCaptureService>();
            var apiKey = ExtractApiKey(http);

            var payload = ValidateGamePayload(request);

            var durationInSeconds = ValidateDuration(request!.DurationInSeconds);

            var result = await service.RecordSession(
                apiKey, durationInSeconds, request.StartedAt, request.GameTitle, payload.GameId, payload.Provider, payload.Platform,
                http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        capture.MapPost("/sessions/start", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<ApiCaptureService>();
            var apiKey = ExtractApiKey(http);

            var payload = ValidateGamePayload(request);

            var result = await service.StartSession(
                apiKey, request!.GameTitle, payload.GameId, payload.Provider, payload.Platform, http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        capture.MapPost("/sessions/ping", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<ApiCaptureService>();
            var apiKey = ExtractApiKey(http);

            var payload = ValidateGamePayload(request);

            var result = await service.PingSession(
                apiKey, request!.GameTitle, payload.GameId, payload.Provider, payload.Platform, http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        capture.MapPost("/sessions/end", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<ApiCaptureService>();
            var apiKey = ExtractApiKey(http);
            var payload = ValidateGamePayload(request);

            var result = await service.EndSessions(
                apiKey,
                request!.GameTitle,
                payload.GameId,
                payload.Provider,
                payload.Platform,
                http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });
    }

    private static string ExtractApiKey(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            throw AppProblemException.Unauthorized("Missing api key.");

        var apiKey = authHeader["Bearer ".Length..].Trim();

        if (apiKey.Length == 0)
            throw AppProblemException.Unauthorized("Missing api key.");

        return apiKey;
    }

    private static async Task<ApiSessionRequest?> ReadRequest(HttpContext context)
    {
        // A chunked body has no Content-Length (.NET's JsonContent sends one): it is read like any other JSON body.
        if (context.Request.ContentLength == 0 || (context.Request.ContentLength is null && !context.Request.HasJsonContentType()))
        {
            return null;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            throw new AppProblemException(413, "The request body is too large.");
        }

        var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (limit is { IsReadOnly: false })
        {
            limit.MaxRequestBodySize = MaxBodyBytes;
        }

        try
        {
            return await context.Request.ReadFromJsonAsync<ApiSessionRequest>(
                context.RequestAborted);
        }
        catch (JsonException)
        {
            throw AppProblemException.Validation("Invalid JSON body.");
        }
    }

    /// <summary>
    /// A recorded session ends now, so its length decides its start: up to 24 hours, like every other session
    /// (2e9 seconds would start it in 1963).
    /// </summary>
    internal static int ValidateDuration(int? durationInSeconds) => durationInSeconds switch
    {
        null or <= 0 => throw AppProblemException.Validation("durationInSeconds must be a positive number."),
        > SessionLimits.MaxDurationSeconds => throw AppProblemException.Validation(
            $"durationInSeconds must be at most {SessionLimits.MaxDurationSeconds} (24 hours)."),
        { } seconds => seconds,
    };

    private static ValidatedGamePayload ValidateGamePayload(ApiSessionRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.GameTitle))
            throw AppProblemException.Validation("gameTitle is required.");
        if (request.GameTitle.Trim().Length > MaxTitleLength)
            throw AppProblemException.Validation($"gameTitle must be at most {MaxTitleLength} characters.");

        var gameId = request.GameId switch
        {
            { ValueKind: JsonValueKind.String } value => value.GetString()?.Trim(),
            { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(gameId))
            throw AppProblemException.Validation("gameId is required.");
        if (gameId.Length > MaxGameIdLength)
            throw AppProblemException.Validation($"gameId must be at most {MaxGameIdLength} characters.");

        var provider = request.GameIdProvider?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider) || !AllowedIdentityProviders.Contains(provider))
            throw AppProblemException.Validation("gameIdProvider must be steam, psn, xbox, or igdb.");

        var platform = request.Platform?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(platform))
            throw AppProblemException.Validation("platform is required.");

        return new ValidatedGamePayload(gameId, provider, platform);
    }

    private sealed record ValidatedGamePayload(string GameId, string Provider, string Platform);
}
