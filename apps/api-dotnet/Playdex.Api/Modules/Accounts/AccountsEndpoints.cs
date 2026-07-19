using System.Security.Claims;
using Playdex.Api.Shared.Auth;

namespace Playdex.Api.Modules.Accounts;

public static class AccountsEndpoints
{
    public static void MapAccountsEndpoints(this RouteGroupBuilder api)
    {
        var accounts = api.MapGroup("/accounts").RequireAuthorization();
        accounts.MapGet("", ListAccounts);

        var sessions = api.MapGroup("/sessions").RequireAuthorization();
        sessions.MapGet("", ListSessions);
        sessions.MapGet("/today", ListSessionsToday);
        sessions.MapGet("/current", ListCurrentSession);
        sessions.MapDelete("/{sessionId:long}", DeleteSession);

        api.MapGet("/stats", ListStatsForRange).RequireAuthorization();

        accounts.MapGet("/{accountId:long}/sessions", ListAccountSessions);
        accounts.MapGet("/{accountId:long}/sessions/today", ListAccountSessionsToday);
        accounts.MapGet("/{accountId:long}/sessions/current", ListAccountCurrentSession);
        accounts.MapGet("/{accountId:long}/stats", ListAccountStats);
    }

    private static async Task<IResult> ListAccounts(
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.ListAccounts(principal.GetUserId(), ct));
    }

    private static async Task<IResult> ListSessions(
        ClaimsPrincipal principal,
        AccountsService service,
        int? limit, int? offset, string? sort, string? direction,
        string? title, string? gameId, string? from, string? to,
        CancellationToken ct)
    {
        var result = await service.ListSessions(
            principal.GetUserId(), null, title, gameId, from, to,
            sort ?? "date", direction ?? "desc",
            Math.Clamp(limit ?? 10, 1, 100), Math.Max(offset ?? 0, 0),
            0, ct);
        return TypedResults.Ok(new AccountSessionsResponseDto(null, result.Sessions, result.Total, result.Limit, result.Offset));
    }

    private static async Task<IResult> ListSessionsToday(
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var sessions = await service.ListSessionsToday(principal.GetUserId(), null, 0, ct);
        return TypedResults.Ok(new AccountTodaySessionsResponseDto(null, sessions));
    }

    private static async Task<IResult> ListCurrentSession(
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var session = await service.LoadCurrentSession(principal.GetUserId(), null, ct);
        return TypedResults.Ok(new AccountCurrentSessionResponseDto(null, session));
    }

    private static async Task<IResult> ListStatsForRange(
        ClaimsPrincipal principal,
        AccountsService service,
        string? from, string? to,
        CancellationToken ct)
    {
        var stats = await service.ComputeStatsForRange(principal.GetUserId(), null, from, to, 0, ct);
        return TypedResults.Ok(new AccountStatsRangeResponseDto(null, stats));
    }

    private static async Task<IResult> ListAccountSessions(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        int? limit, int? offset, string? sort, string? direction,
        string? title, string? gameId, string? from, string? to,
        CancellationToken ct)
    {
        var result = await service.ListSessions(
            principal.GetUserId(), accountId, title, gameId, from, to,
            sort ?? "date", direction ?? "desc",
            Math.Clamp(limit ?? 10, 1, 100), Math.Max(offset ?? 0, 0),
            0, ct);
        return TypedResults.Ok(new AccountSessionsResponseDto(accountId, result.Sessions, result.Total, result.Limit, result.Offset));
    }

    private static async Task<IResult> ListAccountSessionsToday(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var sessions = await service.ListSessionsToday(principal.GetUserId(), accountId, 0, ct);
        return TypedResults.Ok(new AccountTodaySessionsResponseDto(accountId, sessions));
    }

    private static async Task<IResult> ListAccountCurrentSession(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var session = await service.LoadCurrentSession(principal.GetUserId(), accountId, ct);
        return TypedResults.Ok(new AccountCurrentSessionResponseDto(accountId, session));
    }

    private static async Task<IResult> ListAccountStats(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.ComputeAccountStats(principal.GetUserId(), accountId, 0, ct));
    }

    private static async Task<IResult> DeleteSession(
        long sessionId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.DeleteSession(sessionId, principal.GetUserId(), ct));
    }
}
