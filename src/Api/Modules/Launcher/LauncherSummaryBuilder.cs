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

        string Title(long gameId) => games.TryGetValue(gameId, out var game) ? game.Title : "Unknown game";

        var topGames = week
            .GroupBy(session => session.Row.GameId)
            .Select(group => new LauncherTopGameDto(
                GameId.Encode(group.Key), Title(group.Key), group.Sum(session => session.Seconds), group.Count()))
            .OrderByDescending(game => game.TotalSeconds)
            .ThenBy(game => game.Title, StringComparer.Ordinal)
            .Take(TopGameCount)
            .ToList();

        var recent = played
            .OrderByDescending(session => session.Row.StartedAt)
            .Take(RecentSessionCount)
            .Select(session => new LauncherSessionDto(
                session.Row.SessionId.ToString(CultureInfo.InvariantCulture),
                GameId.Encode(session.Row.GameId),
                Title(session.Row.GameId),
                session.Row.PlatformKey,
                session.Row.PlatformName,
                session.Row.StartedAt,
                session.Row.EndedAt,
                session.Seconds))
            .ToList();

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
                    .ToList()))
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
            gameTotals);
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
