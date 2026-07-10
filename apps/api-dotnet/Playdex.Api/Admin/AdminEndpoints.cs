using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Auth;
using Playdex.Api.Common;
using Playdex.Api.Http;

namespace Playdex.Api.Admin;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var admin = api.MapGroup("/admin").RequireAuthorization();

        // Sessions
        admin.MapGet("/sessions/recent", GetRecentSessions);
        admin.MapGet("/sessions", SearchSessions);
        admin.MapPut("/sessions/move", MoveSessions);

        // Providers / Platforms
        admin.MapGet("/providers/platforms", GetPlatformStatuses);
        admin.MapPut("/providers/platforms/status", UpdatePlatformStatus);

        // Credentials
        admin.MapGet("/credentials", ListCredentials);
        admin.MapPut("/credentials/{credentialId:long}", UpdateCredential);

        // Games
        admin.MapGet("/games/search", SearchGames);
        admin.MapPut("/games", UpdateGameBody);
        admin.MapPut("/games/{gameId}", UpdateGameRoute);
    }

    // ── Session Endpoints ────────────────────────────────────────────────

    private static async Task<ApiResponse<AdminRecentSessionsResponseDto>> GetRecentSessions(
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminRecentSessionsResponseDto>.Ok(
            await service.GetRecentSessions(ct));
    }

    private static async Task<ApiResponse<AdminSessionsListResponseDto>> SearchSessions(
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        string? query, int? limit, int? offset,
        CancellationToken ct)
    {
        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminSessionsListResponseDto>.Ok(
            await service.SearchSessions(
                query ?? "",
                Math.Clamp(limit ?? 100, 1, 500),
                Math.Max(offset ?? 0, 0),
                ct));
    }

    private static async Task<ApiResponse<AdminSessionMoveResponseDto>> MoveSessions(
        AdminSessionMoveRequest? request,
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminSessionMoveResponseDto>.Ok(
            await service.MoveSessions(request, ct));
    }

    // ── Platform Endpoints ───────────────────────────────────────────────

    private static async Task<ApiResponse<AdminPlatformStatusesResponseDto>> GetPlatformStatuses(
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminPlatformStatusesResponseDto>.Ok(
            await service.GetPlatformStatuses(ct));
    }

    private static async Task<ApiResponse<AdminPlatformStatusUpdateResponseDto>> UpdatePlatformStatus(
        AdminPlatformStatusUpdateRequest? request,
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminPlatformStatusUpdateResponseDto>.Ok(
            await service.UpdatePlatformStatus(request, ct));
    }

    // ── Credential Endpoints ─────────────────────────────────────────────

    private static async Task<ApiResponse<AdminCredentialsResponseDto>> ListCredentials(
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminCredentialsResponseDto>.Ok(
            await service.ListCredentials(ct));
    }

    private static async Task<ApiResponse<AdminCredentialUpdateResponseDto>> UpdateCredential(
        long credentialId,
        AdminCredentialUpdateRequest? request,
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminCredentialUpdateResponseDto>.Ok(
            await service.UpdateCredential(credentialId, request, ct));
    }

    // ── Game Endpoints ───────────────────────────────────────────────────

    private static async Task<ApiResponse<AdminGameSearchResponseDto>> SearchGames(
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        string? query, int? limit,
        CancellationToken ct)
    {
        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminGameSearchResponseDto>.Ok(
            await service.SearchGames(query ?? "", limit ?? 20, ct));
    }

    /// <summary>
    /// Update a game — gameId comes from the request body.
    /// </summary>
    private static async Task<ApiResponse<AdminGameUpdateResponseDto>> UpdateGameBody(
        AdminGameUpdateRequest? request,
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        await AssertAdmin(principal, authRepo, ct);
        return ApiResponse<AdminGameUpdateResponseDto>.Ok(
            await service.UpdateGame(null, request, ct));
    }

    /// <summary>
    /// Update a game — gameId comes from the route parameter.
    /// The body fields (title, imageUrl, lockTitle, lockImage) override when provided.
    /// </summary>
    private static async Task<ApiResponse<AdminGameUpdateResponseDto>> UpdateGameRoute(
        string gameId,
        AdminGameUpdateRequest? request,
        ClaimsPrincipal principal,
        IAuthRepository authRepo,
        AdminService service,
        CancellationToken ct)
    {
        if (request is null)
            throw AppProblem.Validation("Request body is required.");

        await AssertAdmin(principal, authRepo, ct);

        var parsedId = GameId.Parse(gameId, "gameId");
        return ApiResponse<AdminGameUpdateResponseDto>.Ok(
            await service.UpdateGame(parsedId, request, ct));
    }

    // ── Admin Auth Helper ────────────────────────────────────────────────

    /// <summary>
    /// Verify the current user is an admin. Throws Forbidden if not.
    /// </summary>
    private static async Task AssertAdmin(ClaimsPrincipal principal, IAuthRepository authRepo, CancellationToken ct)
    {
        var userId = GetUserId(principal);
        var user = await authRepo.FindById(userId, ct);

        if (user is null || !user.IsAdmin)
            throw AppProblem.Forbidden();
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
