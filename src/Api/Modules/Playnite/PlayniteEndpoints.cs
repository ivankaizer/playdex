using System.Text.Json;
using Api.Shared.Http;

namespace Api.Modules.Playnite;

public static class PlayniteEndpoints
{
    private static readonly HashSet<string> AllowedGameProviders = ["steam", "unknown"];

    public static void MapPlayniteEndpoints(this RouteGroupBuilder api)
    {
        var playnite = api.MapGroup("/playnite");

        playnite.MapPost("/sessions", async (HttpContext http) =>
        {
            var request = await ReadRequest(http);
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
            var apiKey = ExtractApiKey(http);

            var gameId = ValidateGamePayload(request);

            var durationInSeconds = request!.DurationInSeconds;
            if (durationInSeconds is null or <= 0)
            {
                await TypedResults.Problem("durationInSeconds must be a positive number.", statusCode: 400).ExecuteAsync(http);
                return;
            }

            var result = await service.RecordSession(
                apiKey, durationInSeconds.Value, request.GameTitle, gameId, request.GameProvider, http.RequestAborted);

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

            var gameId = ValidateGamePayload(request);

            var result = await service.StartSession(
                apiKey, request!.GameTitle, gameId, request.GameProvider, http.RequestAborted);

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

            var gameId = ValidateGamePayload(request);

            var result = await service.PingSession(
                apiKey, request!.GameTitle, gameId, request.GameProvider, http.RequestAborted);

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
            var gameId = ValidateGamePayload(request);

            var result = await service.EndSessions(
                apiKey,
                request!.GameTitle,
                gameId,
                request.GameProvider,
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
            throw AppProblem.Unauthorized("Missing api key.");

        var apiKey = authHeader["Bearer ".Length..].Trim();

        if (apiKey.Length == 0)
            throw AppProblem.Unauthorized("Missing api key.");

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
            throw AppProblem.Validation("Invalid JSON body.");
        }
    }

    private static string ValidateGamePayload(PlayniteSessionRequest? request)
    {
        if (string.IsNullOrWhiteSpace(request?.GameTitle))
            throw AppProblem.Validation("gameTitle is required.");

        var gameId = request.GameId switch
        {
            { ValueKind: JsonValueKind.String } value => value.GetString()?.Trim(),
            { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(gameId))
            throw AppProblem.Validation("gameId is required.");

        if (string.IsNullOrWhiteSpace(request.GameProvider) ||
            !AllowedGameProviders.Contains(request.GameProvider.Trim().ToLowerInvariant()))
            throw AppProblem.Validation("gameProvider must be steam or unknown.");

        return gameId;
    }
}
