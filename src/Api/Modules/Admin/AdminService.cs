using Api.Infrastructure.Common;
using Api.Infrastructure.Http;

namespace Api.Modules.Admin;

public sealed class AdminService(AdminRepository repository)
{
    /// <summary>
    /// Returns the last 3 play sessions for every user, grouped by user.
    /// </summary>
    public async Task<AdminRecentSessionsResponseDto> GetRecentSessions(CancellationToken ct)
    {
        var rows = await repository.GetRecentSessions(ct);

        var users = rows
            .GroupBy(r => new { r.UserId, r.Email })
            .Select(g => new AdminUserRecentSessionsDto(
                g.Key.UserId,
                g.Key.Email,
                g.Select(r => new AdminRecentSessionDto(
                    r.SessionId,
                    r.GameName,
                    r.StartedAt,
                    r.DurationSeconds,
                    r.Live
                )).ToList()))
            .ToList();

        return new AdminRecentSessionsResponseDto(users);
    }

    /// <summary>
    /// Search across all users' sessions matching the query on email, game title, or platform.
    /// </summary>
    public async Task<AdminSessionsListResponseDto> SearchSessions(
        string query, int limit, int offset, CancellationToken ct)
    {
        var trimmed = query.Trim();
        var safeLimit = Math.Clamp(limit, 1, 200);
        var safeOffset = Math.Max(0, offset);
        var (sessions, total) = await repository.SearchSessions(trimmed, safeLimit, safeOffset, ct);

        var dtos = sessions.Select(r => new AdminMovableSessionDto(
            r.SessionId,
            r.SourceCredentialId,
            r.UserEmail,
            r.Platform,
            r.AccountUser,
            r.GameName,
            r.StartedAt,
            r.EndedAt,
            r.DurationSeconds,
            r.Live
        )).ToList();

        return new AdminSessionsListResponseDto(trimmed, dtos, total, safeLimit, safeOffset);
    }

    /// <summary>
    /// Move the given sessions to a different platform credential.
    /// </summary>
    public async Task<AdminSessionMoveResponseDto> MoveSessions(
        AdminSessionMoveRequest request, CancellationToken ct)
    {
        if (request.SessionIds.Length == 0)
            throw AppProblemException.Validation("At least one sessionId is required.");

        if (request.TargetCredentialId <= 0)
            throw AppProblemException.Validation("TargetCredentialId must be a positive number.");

        if (await repository.GetCredential(request.TargetCredentialId, ct) is null)
            throw AppProblemException.NotFound("Target credential not found.");

        var moved = await repository.MoveSessions(request.SessionIds, request.TargetCredentialId, ct);

        return new AdminSessionMoveResponseDto(moved, request.TargetCredentialId);
    }

    /// <summary>
    /// List all platform credential statuses grouped by platform key.
    /// </summary>
    public async Task<AdminPlatformStatusesResponseDto> GetPlatformStatuses(CancellationToken ct)
    {
        var rows = await repository.GetPlatformStatuses(ct);

        var dtos = rows.Select(r => new AdminPlatformStatusDto(
            r.Platform,
            r.TotalCredentials,
            r.ActiveCredentials,
            Enabled: r.ActiveCredentials > 0
        )).ToList();

        return new AdminPlatformStatusesResponseDto(dtos);
    }

    /// <summary>
    /// Enable or disable a platform by toggling all its credentials' active state.
    /// </summary>
    public async Task<AdminPlatformStatusUpdateResponseDto> UpdatePlatformStatus(
        AdminPlatformStatusUpdateRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Platform))
            throw AppProblemException.Validation("Platform is required.");

        var platform = request.Platform.Trim();
        if (!await repository.PlatformExists(platform, ct))
            throw AppProblemException.NotFound("Platform not found.");

        var updated = await repository.UpdatePlatformStatus(platform, request.Enabled, ct);

        return new AdminPlatformStatusUpdateResponseDto(platform, request.Enabled, updated);
    }

    /// <summary>
    /// List all platform credentials across all users.
    /// </summary>
    public async Task<AdminCredentialsResponseDto> ListCredentials(CancellationToken ct)
    {
        var rows = await repository.ListCredentials(ct);

        var dtos = rows.Select(r => new AdminCredentialDto(
            r.Id,
            r.UserEmail,
            r.Platform,
            r.ApiKey,
            r.AccountUser,
            r.Active
        )).ToList();

        return new AdminCredentialsResponseDto(dtos);
    }

    /// <summary>
    /// Update a specific platform credential's API key or account user.
    /// </summary>
    public async Task<AdminCredentialUpdateResponseDto> UpdateCredential(
        long credentialId, AdminCredentialChanges changes, CancellationToken ct)
    {
        if (credentialId <= 0)
            throw AppProblemException.Validation("CredentialId must be a positive number.");

        if (!changes.ApiKeyChanged && !changes.AccountUserChanged)
            throw AppProblemException.Validation("At least one field must be provided.");

        var row = await repository.UpdateCredential(
            credentialId,
            changes.ApiKeyChanged,
            changes.ApiKey,
            changes.AccountUserChanged,
            changes.AccountUser,
            ct);

        if (row is null)
            throw AppProblemException.NotFound("Credential not found.");

        var dto = new AdminCredentialDto(
            row.Id, row.UserEmail, row.Platform, row.ApiKey, row.AccountUser, row.Active);

        return new AdminCredentialUpdateResponseDto(dto);
    }

    /// <summary>
    /// Search games by title, capsule URL, or exact game ID.
    /// </summary>
    public async Task<AdminGameSearchResponseDto> SearchGames(string query, int limit, CancellationToken ct)
    {
        var trimmed = query.Trim();

        if (trimmed.Length == 0)
            throw AppProblemException.Validation("query is required.");

        var rows = await repository.SearchGames(trimmed, Math.Clamp(limit, 1, 50), ct);

        var games = rows.Select(r => new AdminGameDto(
            GameId.Encode(r.Id),
            r.Title,
            r.TitleLocked,
            r.Platform,
            r.ImageUrl,
            r.ImageLocked
        )).ToList();

        return new AdminGameSearchResponseDto(trimmed, games);
    }

    /// <summary>
    /// Update a game's title, image URL, and/or lock status.
    /// When gameId is provided in the request body (not route), use this overload.
    /// </summary>
    public async Task<AdminGameUpdateResponseDto> UpdateGame(
        long gameId, AdminGameChanges changes, CancellationToken ct)
    {
        if (!changes.TitleChanged && !changes.ImageUrlChanged &&
            !changes.LockTitleChanged && !changes.LockImageChanged)
            throw AppProblemException.Validation("At least one field must be provided.");

        var row = await repository.UpdateGame(
            gameId,
            changes.TitleChanged,
            changes.Title,
            changes.ImageUrlChanged,
            changes.ImageUrl,
            changes.LockTitleChanged,
            changes.LockTitle,
            changes.LockImageChanged,
            changes.LockImage,
            ct);

        if (row is null)
            throw AppProblemException.NotFound("Game not found.");

        var dto = new AdminGameDto(
            GameId.Encode(row.Id),
            row.Title,
            row.TitleLocked,
            row.PlatformKey ?? "unknown",
            row.CapsuleUrl,
            row.CapsuleUrlLocked
        );

        return new AdminGameUpdateResponseDto(dto);
    }
}
