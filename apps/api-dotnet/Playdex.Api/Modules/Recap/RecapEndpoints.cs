using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Shared.Auth;

namespace Playdex.Api.Modules.Recap;

public static class RecapEndpoints
{
    public static void MapRecapEndpoints(this RouteGroupBuilder api)
    {
        var recap = api.MapGroup("/recap").RequireAuthorization();
        recap.MapGet("/years", GetYears);
        recap.MapGet("", GetRecap);
    }

    private static async Task<IResult> GetYears(
        ClaimsPrincipal principal,
        RecapService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetYears(principal.GetUserId(), ct));
    }

    private static async Task<IResult> GetRecap(
        ClaimsPrincipal principal,
        RecapService service,
        string? from,
        string? to,
        CancellationToken ct)
    {
        DateTimeOffset? startedAt = null;
        DateTimeOffset? endedAt = null;

        if (from is not null || to is not null)
        {
            if (from is null || to is null)
                return TypedResults.Problem("from and to are both required.", statusCode: 400);

            if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedFrom) ||
                !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedTo))
                return TypedResults.Problem("from and to must be valid dates.", statusCode: 400);

            if (parsedFrom > parsedTo)
                return TypedResults.Problem("from must be before to.", statusCode: 400);

            startedAt = new DateTimeOffset(
                parsedFrom.Year, parsedFrom.Month, parsedFrom.Day, 0, 0, 0, TimeSpan.Zero);

            endedAt = new DateTimeOffset(
                parsedTo.Year, parsedTo.Month, parsedTo.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        }

        var result = await service.GetRecap(
            principal.GetUserId(),
            from ?? (startedAt.HasValue ? startedAt.Value.ToString("yyyy-MM-dd") : null),
            to ?? (endedAt.HasValue ? endedAt.Value.AddDays(-1).ToString("yyyy-MM-dd") : null),
            startedAt,
            endedAt,
            ct);

        return TypedResults.Ok(result);
    }
}
