using System.Text.Json;
using Api.Infrastructure.Http;

namespace Api.Modules.Playnite;

public static class PlayniteEndpoints
{
    private static readonly HashSet<string> AllowedGameProviders = ["steam", "psn", "xbox", "igdb"];

    public static void MapPlayniteEndpoints(this RouteGroupBuilder api)
    {
        var playnite = api.MapGroup("/playnite");
        MapSessionEndpoints(playnite);
        var capture = api.MapGroup("/capture");
        MapSessionEndpoints(capture);
    }

    private static void MapSessionEndpoints(RouteGroupBuilder playnite)
    {

        playnite.MapPost("/sessions", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
            var apiKey = ExtractApiKey(http);

            var payload = ValidateGamePayload(request);

            var durationInSeconds = request!.DurationInSeconds;
            if (durationInSeconds is null or <= 0)
            {
                await TypedResults.Problem("durationInSeconds must be a positive number.", statusCode: 400).ExecuteAsync(http);
                return;
            }

            var result = await service.RecordSession(
                apiKey, durationInSeconds.Value, request.GameTitle, payload.GameId, payload.Provider, payload.Platform, http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        playnite.MapPost("/sessions/start", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
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

        playnite.MapPost("/sessions/ping", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
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

        playnite.MapPost("/sessions/end", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
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

    private static async Task<PlayniteSessionRequest?> ReadRequest(HttpContext context)
    {
        if (context.Request.ContentLength is null or 0)
        {
            return null;
        }

        try
        {
            return await context.Request.ReadFromJsonAsync<PlayniteSessionRequest>(
                context.RequestAborted);
        }
        catch (JsonException)
        {
            throw AppProblemException.Validation("Invalid JSON body.");
        }
    }

    private static ValidatedGamePayload ValidateGamePayload(PlayniteSessionRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.GameTitle))
            throw AppProblemException.Validation("gameTitle is required.");

        var gameId = request.GameId switch
        {
            { ValueKind: JsonValueKind.String } value => value.GetString()?.Trim(),
            { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(gameId))
            throw AppProblemException.Validation("gameId is required.");

        var provider = (request.GameIdProvider ?? request.GameProvider)?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(provider) || !AllowedGameProviders.Contains(provider))
            throw AppProblemException.Validation("gameIdProvider must be steam, psn, xbox, or igdb.");

        var platform = request.Platform?.Trim().ToLowerInvariant()
            ?? request.GameProvider?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(platform))
            throw AppProblemException.Validation("platform is required.");

        return new ValidatedGamePayload(gameId, provider, platform);
    }

    private sealed record ValidatedGamePayload(string GameId, string Provider, string Platform);
}
