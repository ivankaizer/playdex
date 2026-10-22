using System.Globalization;
using System.Security.Claims;
using Api.Infrastructure.Auth;
using Api.Modules.Recap;

namespace Api.Modules.Launcher;

public static class LauncherEndpoints
{
    /// <summary>API key only (<see cref="ApiKeyAuthentication.Policy"/>): the launcher has no Playdex sign-in.</summary>
    public static void MapLauncherEndpoints(this RouteGroupBuilder api)
    {
        var launcher = api.MapGroup("/launcher").RequireAuthorization(ApiKeyAuthentication.Policy);
        launcher.MapGet("/summary", Summary);
        launcher.MapGet("/recap", Recap);
    }

    /// <summary>
    /// The web's recap of one calendar year (<see cref="RecapService"/>, the same numbers as its year tabs, in UTC days as
    /// there), for the launcher's Playdex tab.
    /// </summary>
    private static async Task<IResult> Recap(ClaimsPrincipal principal, RecapService recap, int year, CancellationToken ct)
    {
        if (year is < 2000 or > 9998)
        {
            return TypedResults.Problem("year must be a calendar year.", statusCode: 400);
        }
        var from = new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return TypedResults.Ok(await recap.GetRecap(
            principal.GetUserId(),
            from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            from.AddYears(1).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            from,
            from.AddYears(1),
            TimeZoneInfo.Utc,
            ct));
    }

    /// <param name="tz">The launcher's time zone, IANA or Windows id.</param>
    /// <param name="utcOffsetMinutes">Its current UTC offset, used when <paramref name="tz"/> is unknown here.</param>
    private static async Task<IResult> Summary(
        ClaimsPrincipal principal,
        LauncherRepository repository,
        string? tz,
        int? utcOffsetMinutes,
        CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var sessions = await repository.ListSessions(userId, ct);
        var gameIds = sessions.Select(session => session.GameId).Distinct().ToArray();
        var games = await repository.LoadGames(gameIds, ct);
        var identities = await repository.LoadIdentities(gameIds, ct);
        var zone = LauncherSummaryBuilder.ResolveTimeZone(tz, utcOffsetMinutes);
        var summary = LauncherSummaryBuilder.Build(sessions, games, identities, DateTimeOffset.UtcNow, zone);
        return TypedResults.Ok(summary with { SteamPolling = await repository.HasActiveSteamIntegration(userId, ct) });
    }
}
