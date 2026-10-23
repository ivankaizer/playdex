using System.Globalization;
using System.Security.Claims;
using Api.Infrastructure.Auth;
using Api.Infrastructure.Common;

namespace Api.Modules.Recap;

public static class RecapEndpoints
{
    public static void MapRecapEndpoints(this RouteGroupBuilder api)
    {
        var recap = api.MapGroup("/recap").RequireAuthorization();
        recap.MapGet("/years", GetYears);
        recap.MapGet("", GetRecap);
    }

    /// <param name="tz">The client's time zone (IANA or Windows id); UTC when missing.</param>
    private static async Task<IResult> GetYears(
        ClaimsPrincipal principal,
        RecapService service,
        string? tz,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.GetYears(principal.GetUserId(), LocalCalendar.ResolveTimeZone(tz), ct));
    }

    /// <param name="from">First day (or instant) of the range.</param>
    /// <param name="to">Last day of the range, inclusive.</param>
    /// <param name="tz">
    /// The client's time zone (IANA or Windows id): date-only bounds, days, weekdays and hours are its calendar's.
    /// UTC when missing.
    /// </param>
    private static async Task<IResult> GetRecap(
        ClaimsPrincipal principal,
        RecapService service,
        string? from,
        string? to,
        string? tz,
        CancellationToken ct)
    {
        var zone = LocalCalendar.ResolveTimeZone(tz);
        DateTimeOffset? startedAt = null;
        DateTimeOffset? endedAt = null;

        if (from is not null || to is not null)
        {
            if (from is null || to is null)
            {
                return TypedResults.Problem("from and to are required.", statusCode: 400);
            }

            if (!TryParseBound(from, zone, out var parsedFrom) || !TryParseBound(to, zone, out var parsedTo))
            {
                return TypedResults.Problem("from and to must be valid dates.", statusCode: 400);
            }

            if (parsedFrom > parsedTo)
            {
                return TypedResults.Problem("from must be before to.", statusCode: 400);
            }

            startedAt = parsedFrom;
            try
            {
                endedAt = LocalCalendar.StartOfNextDay(parsedTo, zone);
            }
            catch (ArgumentOutOfRangeException)
            {
                return TypedResults.Problem("from and to must be valid dates.", statusCode: 400);
            }
        }

        var result = await service.GetRecap(principal.GetUserId(), from, to, startedAt, endedAt, zone, ct);
        return TypedResults.Ok(result);
    }

    /// <summary>A date-only bound is the start of that day in <paramref name="zone"/>; anything else is an instant.</summary>
    private static bool TryParseBound(string value, TimeZoneInfo zone, out DateTimeOffset bound)
    {
        bound = default;
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            try
            {
                bound = LocalCalendar.StartOf(day, zone);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
        {
            return false;
        }

        bound = instant.ToUniversalTime();
        return true;
    }
}
