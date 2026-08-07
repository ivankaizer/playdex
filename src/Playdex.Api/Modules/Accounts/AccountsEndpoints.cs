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
        sessions.MapDelete("/{sessionId}", DeleteSession);

        api.MapGet("/stats", ListStatsForRange).RequireAuthorization();

        accounts.MapGet("/{accountId}/sessions", ListAccountSessions);
        accounts.MapGet("/{accountId}/sessions/today", ListAccountSessionsToday);
        accounts.MapGet("/{accountId}/sessions/current", ListAccountCurrentSession);
        accounts.MapGet("/{accountId}/stats", ListAccountStats);
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
            ValidateLimit(limit), ValidateOffset(offset),
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
        string accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        int? limit, int? offset, string? sort, string? direction,
        string? title, string? gameId, string? from, string? to,
        CancellationToken ct)
    {
        var parsedAccountId = ParseId(accountId, "accountId");
        var result = await service.ListSessions(
            principal.GetUserId(), parsedAccountId, title, gameId, from, to,
            sort ?? "date", direction ?? "desc",
            ValidateLimit(limit), ValidateOffset(offset),
            0, ct);
        return TypedResults.Ok(new AccountSessionsResponseDto(parsedAccountId, result.Sessions, result.Total, result.Limit, result.Offset));
    }

    private static async Task<IResult> ListAccountSessionsToday(
        string accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var parsedAccountId = ParseId(accountId, "accountId");
        var sessions = await service.ListSessionsToday(principal.GetUserId(), parsedAccountId, 0, ct);
        return TypedResults.Ok(new AccountTodaySessionsResponseDto(parsedAccountId, sessions));
    }

    private static async Task<IResult> ListAccountCurrentSession(
        string accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var parsedAccountId = ParseId(accountId, "accountId");
        var session = await service.LoadCurrentSession(principal.GetUserId(), parsedAccountId, ct);
        return TypedResults.Ok(new AccountCurrentSessionResponseDto(parsedAccountId, session));
    }

    private static async Task<IResult> ListAccountStats(
        string accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.ComputeAccountStats(
            principal.GetUserId(), ParseId(accountId, "accountId"), 0, ct));
    }

    private static async Task<IResult> DeleteSession(
        string sessionId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        return TypedResults.Ok(await service.DeleteSession(
            ParseId(sessionId, "sessionId"), principal.GetUserId(), ct));
    }

    private static int ValidateLimit(int? limit)
    {
        var value = limit ?? 10;
        if (value <= 0)
            throw Playdex.Api.Shared.Http.AppProblem.Validation(
                "limit and offset must be valid numbers.");
        return Math.Min(value, 100);
    }

    private static int ValidateOffset(int? offset)
    {
        var value = offset ?? 0;
        if (value < 0)
            throw Playdex.Api.Shared.Http.AppProblem.Validation(
                "limit and offset must be valid numbers.");
        return value;
    }

    private static long ParseId(string value, string name)
    {
        if (!long.TryParse(value, out var id))
            throw Playdex.Api.Shared.Http.AppProblem.Validation($"{name} must be a number.");
        return id;
    }
}
