namespace Web.Infrastructure.Models;

public sealed record AccountStatsPeriod(
    double TotalSeconds,
    int SessionCount,
    string? TopGameTitle,
    IReadOnlyList<AccountPlatformStat> Platforms,
    IReadOnlyList<AccountTopGame> TopGames,
    IReadOnlyList<AccountMonthStat> Months);

public sealed record AccountPlatformStat(string Platform, double TotalSeconds, string? Source);
public sealed record AccountTopGame(string GameId, string Title, double TotalSeconds);
public sealed record AccountMonthStat(int Year, int Month, double TotalSeconds);
public sealed record AccountStatsRangeResponse(long? AccountId, AccountStatsPeriod Stats);
