using Playdex.Api.Http;

namespace Playdex.Api.Playnite;

public static class PlayniteEndpoints
{
    private static readonly HashSet<string> AllowedGameProviders = ["steam", "unknown"];

    public static void MapPlayniteEndpoints(this RouteGroupBuilder api)
    {
        var playnite = api.MapGroup("/playnite");
        playnite.MapPost("/sessions", RecordSession);
        playnite.MapPost("/sessions/start", StartSession);
        playnite.MapPost("/sessions/ping", PingSession);
        playnite.MapPost("/sessions/end", EndSessions);
    }

    private static async Task<ApiResponse<PlayniteSessionResponseDto>> RecordSession(
        HttpContext context,
        PlayniteSessionRequest? request,
        PlayniteService service,
        CancellationToken ct)
    {
        var apiKey = ExtractApiKey(context);

        ValidateGamePayload(request?.GameTitle, request?.GameId, request?.GameProvider);

        var durationInSeconds = request!.DurationInSeconds;
        if (durationInSeconds is null or <= 0)
        {
            throw AppProblem.Validation("durationInSeconds must be a positive number.");
        }

        var result = await service.RecordSession(
            apiKey, durationInSeconds.Value, request.GameTitle, request.GameId, request.GameProvider, ct);

        if (result is null)
        {
            throw AppProblem.Unauthorized("Invalid api key.");
        }

        return ApiResponse<PlayniteSessionResponseDto>.Ok(result);
    }

    private static async Task<ApiResponse<PlayniteSessionStartResponseDto>> StartSession(
        HttpContext context,
        PlayniteSessionRequest? request,
        PlayniteService service,
        CancellationToken ct)
    {
        var apiKey = ExtractApiKey(context);

        ValidateGamePayload(request?.GameTitle, request?.GameId, request?.GameProvider);

        var result = await service.StartSession(
            apiKey, request!.GameTitle, request.GameId, request.GameProvider, ct);

        if (result is null)
        {
            throw AppProblem.Unauthorized("Invalid api key.");
        }

        return ApiResponse<PlayniteSessionStartResponseDto>.Ok(result);
    }

    private static async Task<ApiResponse<PlayniteSessionPingResponseDto>> PingSession(
        HttpContext context,
        PlayniteSessionRequest? request,
        PlayniteService service,
        CancellationToken ct)
    {
        var apiKey = ExtractApiKey(context);

        ValidateGamePayload(request?.GameTitle, request?.GameId, request?.GameProvider);

        var result = await service.PingSession(
            apiKey, request!.GameTitle, request.GameId, request.GameProvider, ct);

        if (result is null)
        {
            throw AppProblem.Unauthorized("Invalid api key.");
        }

        return ApiResponse<PlayniteSessionPingResponseDto>.Ok(result);
    }

    private static async Task<ApiResponse<PlayniteSessionEndResponseDto>> EndSessions(
        HttpContext context,
        PlayniteService service,
        CancellationToken ct)
    {
        var apiKey = ExtractApiKey(context);

        var result = await service.EndSessions(apiKey, ct);

        if (result is null)
        {
            throw AppProblem.Unauthorized("Invalid api key.");
        }

        return ApiResponse<PlayniteSessionEndResponseDto>.Ok(result);
    }

    private static string ExtractApiKey(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();

        if (string.IsNullOrEmpty(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw AppProblem.Unauthorized("Missing api key.");
        }

        var apiKey = authHeader["Bearer ".Length..].Trim();

        if (apiKey.Length == 0)
        {
            throw AppProblem.Unauthorized("Missing api key.");
        }

        return apiKey;
    }

    private static void ValidateGamePayload(string? gameTitle, string? gameId, string? gameProvider)
    {
        if (string.IsNullOrWhiteSpace(gameTitle))
        {
            throw AppProblem.Validation("gameTitle is required.");
        }

        if (string.IsNullOrWhiteSpace(gameId))
        {
            throw AppProblem.Validation("gameId is required.");
        }

        if (string.IsNullOrWhiteSpace(gameProvider) ||
            !AllowedGameProviders.Contains(gameProvider.Trim().ToLowerInvariant()))
        {
            throw AppProblem.Validation("gameProvider must be steam or unknown.");
        }
    }
}
