using System.Security.Claims;
using Playdex.Api.Shared.Auth;

namespace Playdex.Api.Modules.Games;

public static class GamesEndpoints
{
    public static void MapGamesEndpoints(this RouteGroupBuilder api)
    {
        var games = api.MapGroup("/games").RequireAuthorization();
        games.MapGet("/search", Search);
        games.MapGet("/{gameId}", GetGame);
        games.MapGet("/connections", ListConnections);

        games.MapPost("/connections", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<GameConnectionChangeRequest>();
            var service = http.RequestServices.GetRequiredService<GamesService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.Connect(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });
        games.MapDelete("/connections", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<GameConnectionChangeRequest>();
            var service = http.RequestServices.GetRequiredService<GamesService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.Disconnect(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });

        games.MapGet("/ignored", GetIgnored);
        games.MapPost("/ignored", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<GameIgnoreRequest>();
            var service = http.RequestServices.GetRequiredService<GamesService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.SetIgnore(http.User.GetUserId(), request, true, http.RequestAborted));
            await result.ExecuteAsync(http);
        });
        games.MapDelete("/ignored", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<GameIgnoreRequest>();
            var service = http.RequestServices.GetRequiredService<GamesService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.SetIgnore(http.User.GetUserId(), request, false, http.RequestAborted));
            await result.ExecuteAsync(http);
        });

        games.MapGet("/rating", GetRating);
        games.MapPost("/rating", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<GameRatingRequest>();
            var service = http.RequestServices.GetRequiredService<GamesService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.SetRating(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });
        games.MapDelete("/rating", async (HttpContext http) =>
        {
            var request = await http.Request.ReadFromJsonAsync<GameRatingRequest>();
            var service = http.RequestServices.GetRequiredService<GamesService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.DeleteRating(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });
    }

    private static async Task<IResult> Search(
        ClaimsPrincipal principal,
        GamesService service,
        string? query, int? limit,
        CancellationToken ct)
    {
        var resolvedLimit = limit ?? 10;
        if (resolvedLimit <= 0)
            throw Playdex.Api.Shared.Http.AppProblem.Validation(
                "limit must be a positive integer.");
        return TypedResults.Ok(await service.Search(query ?? "", resolvedLimit, ct));
    }

    private static async Task<IResult> GetGame(
        string gameId,
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetGame(gameId, ct));
    }

    private static async Task<IResult> ListConnections(
        ClaimsPrincipal principal,
        GamesService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.ListConnections(principal.GetUserId(), ct));
    }

    private static async Task<IResult> GetIgnored(
        ClaimsPrincipal principal,
        GamesService service,
        string? gameId,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetIgnore(principal.GetUserId(), gameId, ct));
    }

    private static async Task<IResult> GetRating(
        ClaimsPrincipal principal,
        GamesService service,
        string? gameId,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetRating(principal.GetUserId(), gameId, ct));
    }
}
