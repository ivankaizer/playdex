using System.Security.Claims;
using Api.Infrastructure.Auth;

namespace Api.Modules.Launcher;

public static class LauncherEndpoints
{
    /// <summary>API key only (<see cref="ApiKeyAuthentication.Policy"/>): the launcher has no Playdex sign-in.</summary>
    public static void MapLauncherEndpoints(this RouteGroupBuilder api)
    {
        var launcher = api.MapGroup("/launcher").RequireAuthorization(ApiKeyAuthentication.Policy);
        launcher.MapGet("/summary", Summary);
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
        var sessions = await repository.ListSessions(principal.GetUserId(), ct);
        var gameIds = sessions.Select(session => session.GameId).Distinct().ToArray();
        var games = await repository.LoadGames(gameIds, ct);
        var identities = await repository.LoadIdentities(gameIds, ct);
        var zone = LauncherSummaryBuilder.ResolveTimeZone(tz, utcOffsetMinutes);
        return TypedResults.Ok(LauncherSummaryBuilder.Build(sessions, games, identities, DateTimeOffset.UtcNow, zone));
    }
}
