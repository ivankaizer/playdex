namespace Playdex.Web.Shared.Models;

public sealed record AccountStatsPeriod(
    double TotalSeconds,
    int SessionCount,
    string? TopGameTitle,
    IReadOnlyList<AccountPlatformStat> Platforms,
    IReadOnlyList<AccountTopGame> TopGames);

public sealed record AccountPlatformStat(string Platform, double TotalSeconds, string? Source);
public sealed record AccountTopGame(string GameId, string Title, double TotalSeconds);
public sealed record AccountStatsRangeResponse(long? AccountId, AccountStatsPeriod Stats);
