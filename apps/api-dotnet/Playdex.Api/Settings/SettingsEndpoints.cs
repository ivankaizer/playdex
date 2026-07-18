using System.Security.Claims;
using Playdex.Api.Auth;

namespace Playdex.Api.Settings;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/settings").RequireAuthorization();
        group.MapGet("", GetSettings);
        group.MapPut("", UpdateSettings);
    }

    private static async Task<IResult> GetSettings(
        ClaimsPrincipal principal,
        SettingsService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetSettings(principal.GetUserId(), ct));
    }

    private static async Task<IResult> UpdateSettings(
        UpdateSettingsRequest? request,
        ClaimsPrincipal principal,
        SettingsService service,
        CancellationToken ct)
    {
        if (request is null)
            return TypedResults.Problem("Request body is required.", statusCode: 400);

        return TypedResults.Ok(await service.UpdateSettings(principal.GetUserId(), request, ct));
    }
}
