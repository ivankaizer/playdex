using System.Globalization;
using Api.Infrastructure.Common;

namespace Api.Modules.Launcher;

public sealed record LauncherSessionRow(
    long SessionId, long GameId, string PlatformKey, string PlatformName, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public sealed record LauncherGameRow(long Id, string Title, string? CapsuleUrl);

public sealed record LauncherIdentityRow(long GameId, string Type, string ExternalId);

/// <summary>Builds <see cref="LauncherSummaryDto"/> from the user's sessions; pure, so it is tested without a database.</summary>
public static class LauncherSummaryBuilder
{
    public const int DayCount = 7;
    public const int TopGameCount = 5;
    public const int RecentSessionCount = 20;
    public const int GameDayCount = 14;
    public const int GameSessionCount = 5;

    public static LauncherSummaryDto Build(
        IReadOnlyCollection<LauncherSessionRow> sessions,
        IReadOnlyDictionary<long, LauncherGameRow> games,
        IReadOnlyCollection<LauncherIdentityRow> identities,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var today = LocalDate(now, zone);
        var played = sessions
            .Select(session => (Row: session, Day: LocalDate(session.StartedAt, zone), Seconds: Seconds(session, now)))
            .ToList();

        var days = Enumerable.Range(0, DayCount)
            .Select(offset => today.AddDays(offset - (DayCount - 1)))
            .Select(day =>
            {
                var onDay = played.Where(session => session.Day == day).ToList();
                return new LauncherDayDto(
                    day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), onDay.Sum(session => session.Seconds), onDay.Count);
            })
            .ToList();
        var weekStart = today.AddDays(-(DayCount - 1));
        var week = played.Where(session => session.Day >= weekStart && session.Day <= today).ToList();
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var month = played.Where(session => session.Day >= monthStart && session.Day <= today).ToList();
        var monthDays = Enumerable.Range(0, today.Day)
            .Select(offset => monthStart.AddDays(offset))
            .Select(day =>
            {
                var onDay = month.Where(session => session.Day == day).ToList();
                return new LauncherDayDto(
                    day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), onDay.Sum(session => session.Seconds), onDay.Count);
            })
            .ToList();

        string Title(long gameId) => games.TryGetValue(gameId, out var game) ? game.Title : "Unknown game";

        LauncherPeriodDetailDto Detail(IReadOnlyCollection<(LauncherSessionRow Row, DateOnly Day, long Seconds)> period) => new(
            period.Sum(session => session.Seconds),
            period.Count,
            period
                .GroupBy(session => session.Row.GameId)
                .Select(group => new LauncherTopGameDto(
                    GameId.Encode(group.Key), Title(group.Key), group.Sum(session => session.Seconds), group.Count()))
                .OrderByDescending(game => game.TotalSeconds)
                .ThenBy(game => game.Title, StringComparer.Ordinal)
                .Take(TopGameCount)
                .ToList(),
            period
                .GroupBy(session => session.Row.PlatformKey)
                .Select(group => new LauncherPlatformDto(
                    group.Key, group.First().Row.PlatformName, group.Sum(session => session.Seconds), group.Count()))
                .OrderByDescending(platform => platform.TotalSeconds)
                .ThenBy(platform => platform.PlatformName, StringComparer.Ordinal)
                .ToList());

        var topGames = week
            .GroupBy(session => session.Row.GameId)
            .Select(group => new LauncherTopGameDto(
                GameId.Encode(group.Key), Title(group.Key), group.Sum(session => session.Seconds), group.Count()))
            .OrderByDescending(game => game.TotalSeconds)
            .ThenBy(game => game.Title, StringComparer.Ordinal)
            .Take(TopGameCount)
            .ToList();

        LauncherSessionDto SessionDto((LauncherSessionRow Row, DateOnly Day, long Seconds) session) => new(
            session.Row.SessionId.ToString(CultureInfo.InvariantCulture),
            GameId.Encode(session.Row.GameId),
            Title(session.Row.GameId),
            session.Row.PlatformKey,
            session.Row.PlatformName,
            session.Row.StartedAt,
            session.Row.EndedAt,
            session.Seconds);

        var recent = played
            .OrderByDescending(session => session.Row.StartedAt)
            .Take(RecentSessionCount)
            .Select(SessionDto)
            .ToList();

        // Each game's last 14 days, for its page: only games played in them, so the summary stays small.
        var gameDaysStart = today.AddDays(-(GameDayCount - 1));
        var recentByGame = played
            .Where(session => session.Day >= gameDaysStart && session.Day <= today)
            .GroupBy(session => session.Row.GameId)
            .ToDictionary(group => group.Key, group => new LauncherGameRecentDto(
                Enumerable.Range(0, GameDayCount)
                    .Select(offset => group.Where(session => session.Day == gameDaysStart.AddDays(offset)).Sum(session => session.Seconds))
                    .ToList(),
                group.Count(),
                group.OrderByDescending(session => session.Row.StartedAt).Take(GameSessionCount).Select(SessionDto).ToList()));

        var identitiesByGame = identities.ToLookup(identity => identity.GameId);
        var gameTotals = played
            .GroupBy(session => session.Row.GameId)
            .Select(group => new LauncherGameDto(
                GameId.Encode(group.Key),
                Title(group.Key),
                games.GetValueOrDefault(group.Key)?.CapsuleUrl,
                group.Sum(session => session.Seconds),
                group.Count(),
                group.Max(session => session.Row.StartedAt),
                identitiesByGame[group.Key]
                    .Select(identity => new LauncherGameIdentityDto(identity.Type, identity.ExternalId))
                    .OrderBy(identity => identity.Type, StringComparer.Ordinal)
                    .ThenBy(identity => identity.ExternalId, StringComparer.Ordinal)
                    .ToList(),
                recentByGame.GetValueOrDefault(group.Key)))
            .OrderByDescending(game => game.LastPlayedAt)
            .ToList();

        return new LauncherSummaryDto(
            zone.Id,
            now,
            new LauncherPeriodDto(days[^1].TotalSeconds, days[^1].SessionCount),
            new LauncherPeriodDto(week.Sum(session => session.Seconds), week.Count),
            new LauncherPeriodDto(played.Sum(session => session.Seconds), played.Count),
            Streak(played.Select(session => session.Day).ToHashSet(), today),
            days,
            topGames,
            recent,
            gameTotals,
            new LauncherPeriodsDto(Detail(played.Where(session => session.Day == today).ToList()), Detail(week), Detail(month)),
            monthDays);
    }

    /// <summary>
    /// The time zone the launcher asked for: an IANA or Windows id (<paramref name="timeZone"/>), else a fixed UTC offset
    /// in minutes (clamped to ±14 h), else UTC.
    /// </summary>
    public static TimeZoneInfo ResolveTimeZone(string? timeZone, int? utcOffsetMinutes)
    {
        if (!string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone.Trim(), out var zone))
        {
            return zone;
        }
        if (utcOffsetMinutes is { } minutes)
        {
            var offset = TimeSpan.FromMinutes(Math.Clamp(minutes, -14 * 60, 14 * 60));
            var name = $"UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset.Duration():hh\\:mm}";
            return TimeZoneInfo.CreateCustomTimeZone(name, offset, name, name);
        }
        return TimeZoneInfo.Utc;
    }

    private static DateOnly LocalDate(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    /// <summary>A running session counts up to now.</summary>
    private static long Seconds(LauncherSessionRow session, DateTimeOffset now) =>
        Math.Max(0, (long)((session.EndedAt ?? now) - session.StartedAt).TotalSeconds);

    private static int Streak(HashSet<DateOnly> days, DateOnly today)
    {
        var day = days.Contains(today) ? today : today.AddDays(-1);
        var streak = 0;
        while (days.Contains(day))
        {
            streak++;
            day = day.AddDays(-1);
        }
        return streak;
    }
}
