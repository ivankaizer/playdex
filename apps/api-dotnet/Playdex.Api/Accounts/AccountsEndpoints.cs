using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Accounts;

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

    private static async Task<ApiResponse<List<AccountSummaryDto>>> ListAccounts(
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<List<AccountSummaryDto>>.Ok(await service.ListAccounts(userId, ct));
    }

    private static async Task<ApiResponse<AccountSessionsResponseDto>> ListSessions(
        ClaimsPrincipal principal,
        AccountsService service,
        int? limit, int? offset, string? sort, string? direction,
        string? title, string? gameId, string? from, string? to,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var result = await service.ListSessions(
            userId, null, title, gameId, from, to,
            sort ?? "date", direction ?? "desc",
            Math.Clamp(limit ?? 10, 1, 100), Math.Max(offset ?? 0, 0),
            0, ct);
        return ApiResponse<AccountSessionsResponseDto>.Ok(
            new AccountSessionsResponseDto(null, result.Sessions, result.Total, result.Limit, result.Offset));
    }

    private static async Task<ApiResponse<AccountTodaySessionsResponseDto>> ListSessionsToday(
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var sessions = await service.ListSessionsToday(userId, null, 0, ct);
        return ApiResponse<AccountTodaySessionsResponseDto>.Ok(
            new AccountTodaySessionsResponseDto(null, sessions));
    }

    private static async Task<ApiResponse<AccountCurrentSessionResponseDto>> ListCurrentSession(
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var session = await service.LoadCurrentSession(userId, null, ct);
        return ApiResponse<AccountCurrentSessionResponseDto>.Ok(
            new AccountCurrentSessionResponseDto(null, session));
    }

    private static async Task<ApiResponse<AccountStatsRangeResponseDto>> ListStatsForRange(
        ClaimsPrincipal principal,
        AccountsService service,
        string? from, string? to,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var stats = await service.ComputeStatsForRange(userId, null, from, to, 0, ct);
        return ApiResponse<AccountStatsRangeResponseDto>.Ok(
            new AccountStatsRangeResponseDto(null, stats));
    }

    private static async Task<ApiResponse<AccountSessionsResponseDto>> ListAccountSessions(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        int? limit, int? offset, string? sort, string? direction,
        string? title, string? gameId, string? from, string? to,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var result = await service.ListSessions(
            userId, accountId, title, gameId, from, to,
            sort ?? "date", direction ?? "desc",
            Math.Clamp(limit ?? 10, 1, 100), Math.Max(offset ?? 0, 0),
            0, ct);
        return ApiResponse<AccountSessionsResponseDto>.Ok(
            new AccountSessionsResponseDto(accountId, result.Sessions, result.Total, result.Limit, result.Offset));
    }

    private static async Task<ApiResponse<AccountTodaySessionsResponseDto>> ListAccountSessionsToday(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var sessions = await service.ListSessionsToday(userId, accountId, 0, ct);
        return ApiResponse<AccountTodaySessionsResponseDto>.Ok(
            new AccountTodaySessionsResponseDto(accountId, sessions));
    }

    private static async Task<ApiResponse<AccountCurrentSessionResponseDto>> ListAccountCurrentSession(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var session = await service.LoadCurrentSession(userId, accountId, ct);
        return ApiResponse<AccountCurrentSessionResponseDto>.Ok(
            new AccountCurrentSessionResponseDto(accountId, session));
    }

    private static async Task<ApiResponse<AccountStatsResponseDto>> ListAccountStats(
        long accountId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var stats = await service.ComputeAccountStats(userId, accountId, 0, ct);
        return ApiResponse<AccountStatsResponseDto>.Ok(stats);
    }

    private static async Task<ApiResponse<AccountSessionDeleteResponseDto>> DeleteSession(
        long sessionId,
        ClaimsPrincipal principal,
        AccountsService service,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var result = await service.DeleteSession(sessionId, userId, ct);
        return ApiResponse<AccountSessionDeleteResponseDto>.Ok(result);
    }

    private static long GetUserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw AppProblem.Unauthorized("Invalid token.");
        }
        return userId;
    }
}
