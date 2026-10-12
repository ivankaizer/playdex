using System.Globalization;
using System.Security.Claims;
using Api.Infrastructure.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Api.Modules.Privacy;

public static class PrivacyEndpoints
{
    public static void MapPrivacyEndpoints(this RouteGroupBuilder api)
    {
        var me = api.MapGroup("/me").RequireAuthorization();
        me.MapGet("/export", ExportAccount);
        me.MapDelete("", DeleteAccount);
    }

    private static async Task<IResult> ExportAccount(
        ClaimsPrincipal principal,
        PrivacyService service,
        HttpContext http,
        CancellationToken ct)
    {
        var export = await service.Export(principal.GetUserId(), ct);
        var fileName = $"playdex-export-{export.ExportedAt.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.json";
        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(export);
    }

    private static async Task<IResult> DeleteAccount(
        [FromBody] DeleteAccountRequest? request,
        ClaimsPrincipal principal,
        PrivacyService service,
        CancellationToken ct)
    {
        await service.DeleteAccount(principal.GetUserId(), request?.Password, ct);
        return TypedResults.NoContent();
    }
}
