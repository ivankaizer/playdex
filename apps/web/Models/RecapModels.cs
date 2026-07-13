namespace Playdex.Web.Models;

public sealed record RecapYearsResponse(IReadOnlyList<int> Years);
public sealed record RecapGameStat(
    string GameId,
    string Title,
    string? CapsuleUrl,
    double TotalSeconds,
    int SessionCount,
    double AverageSessionSeconds,
    bool IsAbandoned);
public sealed record RecapPlatformStat(string Platform, int SessionCount, double TotalSeconds, string? Source);
public sealed record MostActiveDay(string Date, int SessionCount);
public sealed record MostActiveMonth(string Month, int SessionCount);
public sealed record WeekdayStat(int Weekday, int SessionCount);
public sealed record HourStat(int Hour, int SessionCount);
public sealed record LongestSessionGame(string GameId, string Title, double SessionSeconds);
public sealed record BingedGame(string GameId, string Title, int SessionCount);
public sealed record PlatformSummary(string Platform, double TotalSeconds, int SessionCount);
public sealed record RecapRange(string? From, string? To);
public sealed record RecapResponse(
    RecapRange Range,
    int TotalSessions,
    double TotalSeconds,
    double AverageSessionSeconds,
    double MedianSessionSeconds,
    double LongestSessionSeconds,
    double ShortestSessionSeconds,
    double AverageSessionsPerDay,
    double AverageSessionsPerWeek,
    double AverageSessionsPerMonth,
    MostActiveDay? MostActiveDay,
    MostActiveMonth? MostActiveMonth,
    int LongestStreakDays,
    int DaysPlayed,
    int DaysNotPlayed,
    IReadOnlyList<WeekdayStat> SessionsByWeekday,
    IReadOnlyList<HourStat> SessionsByHour,
    HourStat? PeakHour,
    double WeekendSeconds,
    double WeekdaySeconds,
    int UniqueGames,
    RecapGameStat? TopGameBySessions,
    RecapGameStat? TopGameByTime,
    IReadOnlyList<RecapGameStat> TopGamesByTime,
    RecapGameStat? TopGameByAverageSession,
    LongestSessionGame? GameWithLongestSession,
    int NewGamesTried,
    int AbandonedGames,
    int ReturnRateGames,
    double TimeOnTop3Percent,
    BingedGame? MostBingedGame,
    IReadOnlyList<RecapPlatformStat> PlatformTotals,
    PlatformSummary? MostUsedPlatform,
    IReadOnlyList<RecapGameStat> GamesTable);
