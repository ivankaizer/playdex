namespace Playdex.Api.Admin;

// ── Repository Intermediate Results ───────────────────────────────────────
// Public records returned by IAdminRepository for Dapper mapping.
// These are raw query results that the service maps to response DTOs.

public sealed record AdminRecentSessionRow(
    long UserId,
    string Email,
    long SessionId,
    string GameName,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    bool Live);

public sealed record AdminMovableSessionRow(
    long SessionId,
    long SourceCredentialId,
    string UserEmail,
    string Platform,
    string? AccountUser,
    string GameName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double DurationSeconds,
    bool Live);

public sealed record AdminPlatformStatusRow(
    string Platform,
    int TotalCredentials,
    int ActiveCredentials);

public sealed record AdminCredentialRow(
    long Id,
    string UserEmail,
    string Platform,
    string? ApiKey,
    string? AccountUser,
    bool Active);

public sealed record AdminGameSearchRow(
    long Id,
    string Title,
    bool TitleLocked,
    string Platform,
    string? ImageUrl,
    bool ImageLocked);

public sealed record AdminGameUpdateRow(
    long Id,
    string Title,
    bool TitleLocked,
    string? CapsuleUrl,
    bool CapsuleUrlLocked,
    long PlatformId,
    string? PlatformKey);

// ── Recent Sessions ──────────────────────────────────────────────────────

public sealed record AdminRecentSessionDto(
    long SessionId,
    string GameName,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    bool Live);

public sealed record AdminUserRecentSessionsDto(
    long UserId,
    string Email,
    IReadOnlyList<AdminRecentSessionDto> Sessions);

public sealed record AdminRecentSessionsResponseDto(
    IReadOnlyList<AdminUserRecentSessionsDto> Users);

// ── Searchable Sessions ──────────────────────────────────────────────────

public sealed record AdminMovableSessionDto(
    long SessionId,
    long SourceCredentialId,
    string UserEmail,
    string Platform,
    string? AccountUser,
    string GameName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    double DurationSeconds,
    bool Live);

public sealed record AdminSessionsListResponseDto(
    string Query,
    IReadOnlyList<AdminMovableSessionDto> Sessions,
    int Total,
    int Limit,
    int Offset);

// ── Session Move ─────────────────────────────────────────────────────────

public sealed record AdminSessionMoveRequest(
    long[] SessionIds,
    long TargetCredentialId);

public sealed record AdminSessionMoveResponseDto(
    int MovedSessions,
    long TargetCredentialId);

// ── Provider Platforms ───────────────────────────────────────────────────

public sealed record AdminPlatformStatusDto(
    string Platform,
    int TotalCredentials,
    int ActiveCredentials,
    bool Enabled);

public sealed record AdminPlatformStatusesResponseDto(
    IReadOnlyList<AdminPlatformStatusDto> Platforms);

public sealed record AdminPlatformStatusUpdateRequest(
    string Platform,
    bool Enabled);

public sealed record AdminPlatformStatusUpdateResponseDto(
    string Platform,
    bool Enabled,
    int UpdatedCredentials);

// ── Credentials ──────────────────────────────────────────────────────────

public sealed record AdminCredentialDto(
    long Id,
    string UserEmail,
    string Platform,
    string? ApiKey,
    string? AccountUser,
    bool Active);

public sealed record AdminCredentialsResponseDto(
    IReadOnlyList<AdminCredentialDto> Credentials);

public sealed record AdminCredentialUpdateRequest(
    string? ApiKey,
    string? AccountUser);

public sealed record AdminCredentialUpdateResponseDto(
    AdminCredentialDto Credential);

// ── Games ────────────────────────────────────────────────────────────────

public sealed record AdminGameDto(
    string Id,
    string Title,
    bool TitleLocked,
    string Platform,
    string? ImageUrl,
    bool ImageLocked);

public sealed record AdminGameSearchResponseDto(
    string Query,
    IReadOnlyList<AdminGameDto> Games);

public sealed record AdminGameUpdateRequest(
    string? GameId,
    string? Title,
    string? ImageUrl,
    bool? LockTitle,
    bool? LockImage);

public sealed record AdminGameUpdateResponseDto(
    AdminGameDto Game);
