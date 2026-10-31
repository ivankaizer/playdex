namespace Api.Modules.Launcher;

/// <summary>
/// Everything the Windows launcher shows from Playdex, in one response, computed in the launcher's time zone.
/// Days are local calendar days; a session counts toward the day it started. Sessions the user's "ignore short
/// sessions" setting hides are left out everywhere.
/// </summary>
/// <param name="TimeZone">The time zone the days were computed in (what was resolved from the request).</param>
/// <param name="Days">The last 7 local days, oldest first; the last one is today.</param>
/// <param name="Week">Those 7 days together.</param>
/// <param name="TopGames">The most played games of those 7 days, at most 5.</param>
/// <param name="RecentSessions">The newest sessions, at most 20, newest first.</param>
/// <param name="Games">Every game the user has played, most recently played first, with its identities so the launcher can match its own games.</param>
/// <param name="CurrentStreakDays">Consecutive days played, ending today (or yesterday if nothing was played yet today).</param>
/// <param name="Periods">Today, those 7 days and this calendar month in detail: the launcher's Playdex overview.</param>
/// <param name="MonthDays">This calendar month's local days, from the 1st to today.</param>
/// <param name="SteamPolling">
/// The user has an active Steam integration, so Playdex records Steam games by polling; the launcher then starts them
/// without recording them itself, which would count each session twice. For launchers older than
/// <paramref name="PolledProviders"/>.
/// </param>
/// <param name="PolledProviders">
/// The game id providers ("steam", "psn", "xbox") of the user's active polling integrations: Playdex records the games
/// played on those accounts by polling, so the launcher starts a game recorded under one of them (an Xbox app game)
/// without recording it itself.
/// </param>
public sealed record LauncherSummaryDto(
    string TimeZone,
    DateTimeOffset GeneratedAt,
    LauncherPeriodDto Today,
    LauncherPeriodDto Week,
    LauncherPeriodDto AllTime,
    int CurrentStreakDays,
    IReadOnlyList<LauncherDayDto> Days,
    IReadOnlyList<LauncherTopGameDto> TopGames,
    IReadOnlyList<LauncherSessionDto> RecentSessions,
    IReadOnlyList<LauncherGameDto> Games,
    LauncherPeriodsDto Periods,
    IReadOnlyList<LauncherDayDto> MonthDays,
    bool SteamPolling = false,
    IReadOnlyList<string>? PolledProviders = null);

public sealed record LauncherPeriodsDto(LauncherPeriodDetailDto Today, LauncherPeriodDetailDto Week, LauncherPeriodDetailDto Month);

/// <param name="TopGames">The period's most played games, at most 5.</param>
/// <param name="Platforms">Time per platform, most played first, with names from Playdex's catalog.</param>
public sealed record LauncherPeriodDetailDto(
    long TotalSeconds,
    int SessionCount,
    IReadOnlyList<LauncherTopGameDto> TopGames,
    IReadOnlyList<LauncherPlatformDto> Platforms);

public sealed record LauncherPlatformDto(string PlatformKey, string PlatformName, long TotalSeconds, int SessionCount);

public sealed record LauncherPeriodDto(long TotalSeconds, int SessionCount);

/// <param name="Date">Local date, yyyy-MM-dd.</param>
public sealed record LauncherDayDto(string Date, long TotalSeconds, int SessionCount);

public sealed record LauncherTopGameDto(string GameId, string Title, long TotalSeconds, int SessionCount);

/// <param name="PlatformName">The platform's display name from Playdex's catalog ("Steam Deck"), so clients don't keep their own.</param>
/// <param name="EndedAt">Null while the session is running.</param>
public sealed record LauncherSessionDto(
    string SessionId,
    string GameId,
    string Title,
    string PlatformKey,
    string PlatformName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    long DurationSeconds);

/// <param name="Recent">The game's last 14 local days, for its page in the launcher; null when it wasn't played in them.</param>
public sealed record LauncherGameDto(
    string GameId,
    string Title,
    string? CapsuleUrl,
    long TotalSeconds,
    int SessionCount,
    DateTimeOffset LastPlayedAt,
    IReadOnlyList<LauncherGameIdentityDto> Identities,
    LauncherGameRecentDto? Recent = null);

/// <param name="DaySeconds">Seconds played on each of the last 14 local days, oldest first; the last is today.</param>
/// <param name="SessionCount">Sessions in those days.</param>
/// <param name="Sessions">The newest of them, at most 5, newest first.</param>
public sealed record LauncherGameRecentDto(IReadOnlyList<long> DaySeconds, int SessionCount, IReadOnlyList<LauncherSessionDto> Sessions);

/// <param name="Type">steam, psn, xbox, igdb, gog or epic: what the capture routes call gameIdProvider.</param>
/// <param name="ExternalId">The id the game was captured with (a Steam app id, or what a client sent).</param>
public sealed record LauncherGameIdentityDto(string Type, string ExternalId);
