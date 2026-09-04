namespace Api.Modules.Accounts;

public sealed record AccountSummaryDto(long Id, string Name);

public sealed record AccountSessionDto(
    long SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string GameId,
    string PlatformKey,
    string PlatformName,
    string Game,
    string? CapsuleUrl,
    bool Ignored,
    string? MappedPlatform,
    string? MappedSource);

public sealed record AccountSessionsResponseDto(
    long? AccountId,
    IReadOnlyList<AccountSessionDto> Sessions,
    int Total,
    int Limit,
    int Offset);

public sealed record AccountTodaySessionsResponseDto(
    long? AccountId,
    IReadOnlyList<AccountSessionDto> Sessions);

public sealed record AccountCurrentSessionResponseDto(
    long? AccountId,
    AccountSessionDto? Session);

public sealed record AccountSessionDeleteResponseDto(long SessionId);

public sealed record AccountSessionUpdateRequest(
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public sealed record AccountSessionUpdateResponseDto(
    long SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt);

public sealed record AccountStatsPeriodDto(
    double TotalSeconds,
    int SessionCount,
    string? TopGameTitle,
    IReadOnlyList<AccountPlatformStatDto> Platforms,
    IReadOnlyList<AccountTopGameDto> TopGames,
    IReadOnlyList<AccountMonthStatDto> Months);

public sealed record AccountPlatformStatDto(
    string PlatformKey,
    string PlatformName,
    double TotalSeconds,
    string? Source);

public sealed record AccountTopGameDto(string GameId, string Title, double TotalSeconds);

public sealed record AccountMonthStatDto(int Year, int Month, double TotalSeconds);

public sealed record AccountStatsResponseDto(
    long? AccountId,
    AccountStatsPeriodDto Monthly,
    AccountStatsPeriodDto Yearly);

public sealed record AccountStatsRangeResponseDto(
    long? AccountId,
    AccountStatsPeriodDto Stats);
