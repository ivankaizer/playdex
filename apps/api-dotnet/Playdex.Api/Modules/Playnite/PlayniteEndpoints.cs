using System.Text.Json;
using Playdex.Api.Shared.Http;

namespace Playdex.Api.Modules.Playnite;

public static class PlayniteEndpoints
{
    private static readonly HashSet<string> AllowedGameProviders = ["steam", "unknown"];

    public static void MapPlayniteEndpoints(this RouteGroupBuilder api)
    {
        var playnite = api.MapGroup("/playnite");

        playnite.MapPost("/sessions", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<PlayniteSessionRequest>();
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
            var apiKey = ExtractApiKey(http);

            ValidateGamePayload(request?.GameTitle, request?.GameId, request?.GameProvider);

            var durationInSeconds = request!.DurationInSeconds;
            if (durationInSeconds is null or <= 0)
            {
                await TypedResults.Problem("durationInSeconds must be a positive number.", statusCode: 400).ExecuteAsync(http);
                return;
            }

            var result = await service.RecordSession(
                apiKey, durationInSeconds.Value, request.GameTitle, request.GameId, request.GameProvider, http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        playnite.MapPost("/sessions/start", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<PlayniteSessionRequest>();
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
            var apiKey = ExtractApiKey(http);

            ValidateGamePayload(request?.GameTitle, request?.GameId, request?.GameProvider);

            var result = await service.StartSession(
                apiKey, request!.GameTitle, request.GameId, request.GameProvider, http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        playnite.MapPost("/sessions/ping", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<PlayniteSessionRequest>();
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
            var apiKey = ExtractApiKey(http);

            ValidateGamePayload(request?.GameTitle, request?.GameId, request?.GameProvider);

            var result = await service.PingSession(
                apiKey, request!.GameTitle, request.GameId, request.GameProvider, http.RequestAborted);

            if (result is null)
            {
                await TypedResults.Problem("Invalid api key.", statusCode: 401).ExecuteAsync(http);
                return;
            }

            await TypedResults.Ok(result).ExecuteAsync(http);
        });

        playnite.MapPost("/sessions/end", async (HttpContext http) =>
        {
            var service = http.RequestServices.GetRequiredService<PlayniteService>();
            var apiKey = ExtractApiKey(http);

            var result = await service.EndSessions(apiKey, http.RequestAborted);

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

    private static void ValidateGamePayload(string? gameTitle, string? gameId, string? gameProvider)
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
            throw AppProblem.Validation("gameTitle is required.");

        if (string.IsNullOrWhiteSpace(gameId))
            throw AppProblem.Validation("gameId is required.");

        if (string.IsNullOrWhiteSpace(gameProvider) ||
            !AllowedGameProviders.Contains(gameProvider.Trim().ToLowerInvariant()))
            throw AppProblem.Validation("gameProvider must be steam or unknown.");
    }
}
