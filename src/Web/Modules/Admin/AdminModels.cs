using System.Text.Json.Serialization;

namespace Web.Modules.Admin;

public sealed record AdminRecentSession(
    long SessionId,
    string GameName,
    DateTimeOffset StartedAt,
    double DurationSeconds,
    bool Live);
public sealed record AdminUserRecentSessions(long UserId, string Email, IReadOnlyList<AdminRecentSession> Sessions);
public sealed record AdminRecentSessionsResponse(IReadOnlyList<AdminUserRecentSessions> Users);
public sealed record AdminMovableSession(
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
public sealed record AdminSessionsListResponse(
    string Query,
    IReadOnlyList<AdminMovableSession> Sessions,
    int Total,
    int Limit,
    int Offset);
public sealed record AdminSessionMoveRequest(IReadOnlyList<long> SessionIds, long TargetCredentialId);
public sealed record AdminSessionMoveResponse(int MovedSessions, long TargetCredentialId);
public sealed record AdminPlatformStatus(
    string Platform,
    int TotalCredentials,
    int ActiveCredentials,
    bool Enabled);
public sealed record AdminPlatformStatusesResponse(IReadOnlyList<AdminPlatformStatus> Platforms);
public sealed record AdminPlatformStatusUpdateRequest(string Platform, bool Enabled);
public sealed record AdminPlatformStatusUpdateResponse(string Platform, bool Enabled, int UpdatedCredentials);
public sealed record AdminCredential(
    long Id,
    string UserEmail,
    string Platform,
    string? ApiKey,
    [property: JsonPropertyName("user")] string? AccountUser,
    bool Active);
public sealed record AdminCredentialsResponse(IReadOnlyList<AdminCredential> Credentials);
public sealed record AdminCredentialUpdateRequest(
    string? ApiKey,
    [property: JsonPropertyName("user")] string? AccountUser);
public sealed record AdminCredentialUpdateResponse(AdminCredential Credential);
public sealed record AdminGame(
    string Id,
    string Title,
    bool TitleLocked,
    string Platform,
    string? ImageUrl,
    bool ImageLocked);
public sealed record AdminGameSearchResponse(string Query, IReadOnlyList<AdminGame> Games);
public sealed record AdminGameUpdateRequest(
    string? GameId,
    string? Title,
    string? ImageUrl,
    bool? LockTitle,
    bool? LockImage);
public sealed record AdminGameUpdateResponse(AdminGame Game);
