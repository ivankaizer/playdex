namespace Playdex.Api.Recap;

public sealed record RecapYearsResponseDto(IReadOnlyList<int> Years);

public sealed record RecapGameStatDto(
    string GameId,
    string Title,
    string? CapsuleUrl,
    double TotalSeconds,
    int SessionCount,
    double AverageSessionSeconds,
    bool IsAbandoned);

public sealed record RecapPlatformStatDto(
    string Platform,
    int SessionCount,
    double TotalSeconds,
    string? Source);

public sealed record MostActiveDayDto(string Date, int SessionCount);

public sealed record MostActiveMonthDto(string Month, int SessionCount);

public sealed record WeekdayStatDto(int Weekday, int SessionCount);

public sealed record HourStatDto(int Hour, int SessionCount);

public sealed record LongestSessionGameDto(
    string GameId,
    string Title,
    double SessionSeconds);

public sealed record BingedGameDto(
    string GameId,
    string Title,
    int SessionCount);

public sealed record PlatformSummaryDto(
    string Platform,
    double TotalSeconds,
    int SessionCount);

public sealed record RecapRangeDto(string? From, string? To);

public sealed record RecapResponseDto(
    RecapRangeDto Range,
    int TotalSessions,
    double TotalSeconds,
    double AverageSessionSeconds,
    double MedianSessionSeconds,
    double LongestSessionSeconds,
    double ShortestSessionSeconds,
    double AverageSessionsPerDay,
    double AverageSessionsPerWeek,
    double AverageSessionsPerMonth,
    MostActiveDayDto? MostActiveDay,
    MostActiveMonthDto? MostActiveMonth,
    int LongestStreakDays,
    int DaysPlayed,
    int DaysNotPlayed,
    IReadOnlyList<WeekdayStatDto> SessionsByWeekday,
    IReadOnlyList<HourStatDto> SessionsByHour,
    HourStatDto? PeakHour,
    double WeekendSeconds,
    double WeekdaySeconds,
    int UniqueGames,
    RecapGameStatDto? TopGameBySessions,
    RecapGameStatDto? TopGameByTime,
    IReadOnlyList<RecapGameStatDto> TopGamesByTime,
    RecapGameStatDto? TopGameByAverageSession,
    LongestSessionGameDto? GameWithLongestSession,
    int NewGamesTried,
    int AbandonedGames,
    int ReturnRateGames,
    double TimeOnTop3Percent,
    BingedGameDto? MostBingedGame,
    IReadOnlyList<RecapPlatformStatDto> PlatformTotals,
    PlatformSummaryDto? MostUsedPlatform,
    IReadOnlyList<RecapGameStatDto> GamesTable);
