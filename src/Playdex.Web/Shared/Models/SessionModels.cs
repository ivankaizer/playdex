namespace Playdex.Web.Shared.Models;

public sealed record AccountSession(
    long SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string GameId,
    string Platform,
    string Game,
    string? CapsuleUrl,
    bool Ignored,
    string? MappedPlatform,
    string? MappedSource);

public sealed record AccountSessionsResponse(
    long? AccountId,
    IReadOnlyList<AccountSession> Sessions,
    int Total,
    int Limit,
    int Offset);

public sealed record AccountTodaySessionsResponse(long? AccountId, IReadOnlyList<AccountSession> Sessions);
public sealed record AccountCurrentSessionResponse(long? AccountId, AccountSession? Session);
