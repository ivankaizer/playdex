using System.Security.Claims;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;

namespace Api.Modules.Manual;

public static class ManualEndpoints
{
    public static void MapManualEndpoints(this RouteGroupBuilder api)
    {
        var manual = api.MapGroup("/manual").RequireAuthorization();

        manual.MapPost("/sessions/start", async (HttpContext http) =>
        {
            var request = await http.Request.ReadJsonBodyAsync<ManualSessionStartRequest>();
            var service = http.RequestServices.GetRequiredService<ManualService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.StartSession(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });

        manual.MapPost("/sessions/stop", StopSession);
        manual.MapGet("/sessions/current", GetCurrentSession);
        manual.MapGet("/platforms", ListPlatforms);
        manual.MapGet("/search", SearchMapping);

        manual.MapPost("/entries", async (HttpContext http) =>
        {
            var request = await http.Request.ReadJsonBodyAsync<ManualEntryCreateRequest>();
            var service = http.RequestServices.GetRequiredService<ManualService>();
            IResult result = request is null
                ? TypedResults.Problem("Request body is required.", statusCode: 400)
                : TypedResults.Ok(await service.CreateEntry(http.User.GetUserId(), request, http.RequestAborted));
            await result.ExecuteAsync(http);
        });

    }

    private static async Task<IResult> StopSession(
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.StopSession(principal.GetUserId(), ct));
    }

    private static async Task<IResult> GetCurrentSession(
        ClaimsPrincipal principal,
        ManualService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetCurrentSession(principal.GetUserId(), ct));
    }

    private static async Task<IResult> ListPlatforms(
        ManualService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.ListPlatforms(ct));
    }

    private static async Task<IResult> SearchMapping(
        ClaimsPrincipal principal,
        ManualService service,
        string? query,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.SearchMapping(query ?? "", ct));
    }
}
