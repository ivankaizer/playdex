using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Games;

public static class GamesEndpoints
{
    public static void MapGamesEndpoints(this RouteGroupBuilder api)
    {
        var games = api.MapGroup("/games").RequireAuthorization();
        games.MapGet("/search", Search);
        games.MapGet("/{gameId}", GetGame);
        games.MapGet("/connections", ListConnections);
        games.MapPost("/connections", Connect);
        games.MapDelete("/connections", Disconnect);
        games.MapGet("/ignored", GetIgnored);
        games.MapPost("/ignored", SetIgnored);
        games.MapDelete("/ignored", Unignore);
        games.MapGet("/rating", GetRating);
        games.MapPost("/rating", UpsertRating);
        games.MapDelete("/rating", RemoveRating);
    }

    private static async Task<ApiResponse<GameSearchResponseDto>> Search(
        ClaimsPrincipal principal,
        GamesService service,
        string? query, int? limit,
        CancellationToken ct)
    {
        return ApiResponse<GameSearchResponseDto>.Ok(
            await service.Search(query ?? "", limit ?? 20, ct));
    }

    private static async Task<ApiResponse<GameResponseDto>> GetGame(
        string gameId,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        return ApiResponse<GameResponseDto>.Ok(await service.GetGame(gameId, ct));
    }

    private static async Task<ApiResponse<GameConnectionListResponseDto>> ListConnections(
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<GameConnectionListResponseDto>.Ok(
            await service.ListConnections(userId, ct));
    }

    private static async Task<ApiResponse<GameConnectionChangeResponseDto>> Connect(
        GameConnectionChangeRequest? request,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        var userId = GetUserId(principal);
        return ApiResponse<GameConnectionChangeResponseDto>.Ok(
            await service.Connect(userId, request, ct));
    }

    private static async Task<ApiResponse<GameConnectionChangeResponseDto>> Disconnect(
        GameConnectionChangeRequest? request,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        var userId = GetUserId(principal);
        return ApiResponse<GameConnectionChangeResponseDto>.Ok(
            await service.Disconnect(userId, request, ct));
    }

    private static async Task<ApiResponse<GameIgnoreResponseDto>> GetIgnored(
        ClaimsPrincipal principal,
        GamesService service,
        string? gameId,
        CancellationToken ct)
    {
        return ApiResponse<GameIgnoreResponseDto>.Ok(
            await service.GetIgnore(GetUserId(principal), gameId, ct));
    }

    private static async Task<ApiResponse<GameIgnoreResponseDto>> SetIgnored(
        GameIgnoreRequest? request,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        return ApiResponse<GameIgnoreResponseDto>.Ok(
            await service.SetIgnore(GetUserId(principal), request, true, ct));
    }

    private static async Task<ApiResponse<GameIgnoreResponseDto>> Unignore(
        GameIgnoreRequest? request,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        return ApiResponse<GameIgnoreResponseDto>.Ok(
            await service.SetIgnore(GetUserId(principal), request, false, ct));
    }

    private static async Task<ApiResponse<GameRatingResponseDto>> GetRating(
        ClaimsPrincipal principal,
        GamesService service,
        string? gameId,
        CancellationToken ct)
    {
        return ApiResponse<GameRatingResponseDto>.Ok(
            await service.GetRating(GetUserId(principal), gameId, ct));
    }

    private static async Task<ApiResponse<GameRatingResponseDto>> UpsertRating(
        GameRatingRequest? request,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        return ApiResponse<GameRatingResponseDto>.Ok(
            await service.SetRating(GetUserId(principal), request, ct));
    }

    private static async Task<ApiResponse<GameRatingResponseDto>> RemoveRating(
        GameRatingRequest? request,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        if (request is null) throw AppProblem.Validation("Request body is required.");
        return ApiResponse<GameRatingResponseDto>.Ok(
            await service.DeleteRating(GetUserId(principal), request, ct));
    }

    private static long GetUserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw AppProblem.Unauthorized("Invalid token.");
        }
        return userId;
    }
}
