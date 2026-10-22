using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Common;
using Playdex.Persistence;
using Api.Modules.Games;

namespace Api.Modules.Accounts;

public sealed class AccountsRepository(PlaydexDbContext db, IGames games)
{
    public async Task<List<AccountSummaryDto>> ListAccounts(long userId, CancellationToken ct)
    {
        return await db.Integrations
            .AsNoTracking()
            .Where(pc => pc.UserId == userId)
            .Select(pc => new AccountSummaryDto(pc.Id,
                pc.Type == IntegrationType.Steam ? "steam" :
                pc.Type == IntegrationType.Psn ? "psn" :
                pc.Type == IntegrationType.Xbox ? "xbox" : "api"))
            .OrderBy(account => account.Id)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyCollection<long>> ResolveConnectedGameIds(
        long userId,
        long gameId,
        CancellationToken ct)
    {
        var edges = await db.GameConnections
            .AsNoTracking()
            .Where(edge => edge.UserId == userId)
            .Select(edge => new { edge.GameId, edge.ConnectedGameId })
            .ToListAsync(ct);
        return games.ResolveConnectedIds(
            gameId,
            edges.Select(edge => (edge.GameId, edge.ConnectedGameId)).ToList());
    }

    public async Task<SessionQueryResult> ListSessions(
        long userId, long? accountId, string? title, IReadOnlyCollection<long>? gameIds,
        string? platform, DateTimeOffset? from, DateTimeOffset? to,
        string sort, string direction, int limit, int offset,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var query = BuildSessionQuery(
            userId, accountId, title, gameIds, platform, from, to, excludeIgnored: false);

        var total = await query.CountAsync(ct);

        var ascending = direction.Equals("asc", StringComparison.OrdinalIgnoreCase);
        var ordered = sort switch
        {
            "duration" => ascending
                ? query.OrderBy(row => (row.Session.EndedAt ?? DateTimeOffset.UtcNow) - row.Session.StartedAt)
                : query.OrderByDescending(row => (row.Session.EndedAt ?? DateTimeOffset.UtcNow) - row.Session.StartedAt),
            _ => ascending
                ? query.OrderBy(row => row.Session.StartedAt)
                : query.OrderByDescending(row => row.Session.StartedAt),
        };
        var sessions = await ordered.Skip(offset).Take(limit).Select(ToSessionDbRow).ToListAsync(ct);

        return new SessionQueryResult(total, limit, offset, sessions.Select(MapSession).ToList());
    }

    /// <summary>How many of today's sessions <see cref="ListSessionsToday"/> lists; its total counts them all.</summary>
    public const int TodaySessionLimit = 10;

    /// <summary>
    /// Sessions that overlap the client's current day: one that crossed midnight or is still running counts too.
    /// The newest <see cref="TodaySessionLimit"/> are listed, and the total counts every one of them.
    /// </summary>
    public async Task<SessionQueryResult> ListSessionsToday(
        long userId, long? accountId, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var today = LocalCalendar.DateOf(now, zone);
        var todayStart = LocalCalendar.StartOf(today, zone);
        var todayEnd = LocalCalendar.StartOf(today.AddDays(1), zone);

        var query = BuildSessionQuery(
                userId, accountId, null, null, null, null, null, excludeIgnored: false)
            .Where(row => row.Session.StartedAt < todayEnd && (row.Session.EndedAt ?? now) > todayStart);
        var total = await query.CountAsync(ct);
        var sessions = await query
            .OrderByDescending(row => row.Session.StartedAt)
            .Take(TodaySessionLimit)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);

        return new SessionQueryResult(total, TodaySessionLimit, 0, sessions.Select(MapSession).ToList());
    }

    public async Task<AccountSessionDto?> LoadCurrentSession(
        long userId, long? accountId, CancellationToken ct)
    {
        var session = await BuildSessionQuery(
                userId, accountId, null, null, null, null, null, excludeIgnored: false)
            .Where(row => row.Session.EndedAt == null)
            .OrderByDescending(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .FirstOrDefaultAsync(ct);
        return session is null ? null : MapSession(session);
    }

    public async Task<long> DeleteSession(long sessionId, long userId, CancellationToken ct)
    {
        var affected = await db.PlaySessions
            .Where(session => session.Id == sessionId && session.UserId == userId)
            .ExecuteDeleteAsync(ct);
        return affected == 1 ? sessionId : 0;
    }

    public Task<PlaySession?> GetSession(long sessionId, CancellationToken ct) =>
        db.PlaySessions.SingleOrDefaultAsync(session => session.Id == sessionId, ct);

    public Task SaveChanges(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<List<AccountSessionDto>> ListSessionsForStats(
        long userId, long? accountId, DateTimeOffset? from, DateTimeOffset? to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var sessions = await LoadStatsRows(userId, accountId, null, from, to, ct);
        return sessions.Select(MapSession).ToList();
    }

    private Task<List<SessionDbRow>> LoadStatsRows(
        long userId, long? accountId, string? title, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct) =>
        BuildSessionQuery(userId, accountId, title, null, null, from, to, excludeIgnored: true)
            .OrderBy(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);

    /// <summary>
    /// Totals per year and month of the client's calendar, from the year of the first session (not a fixed start)
    /// through the current year.
    /// </summary>
    public async Task<GameStatsResponseDto> ComputeGameStats(
        long userId,
        IReadOnlyCollection<long> gameIds,
        TimeZoneInfo zone,
        CancellationToken ct)
    {
        var sessions = await BuildSessionQuery(
                userId, null, null, gameIds, null, null, null, excludeIgnored: true)
            .OrderBy(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);
        if (sessions.Count == 0)
        {
            return new GameStatsResponseDto(0, 0, null, [], []);
        }

        var now = DateTimeOffset.UtcNow;
        var played = sessions
            .Select(session => (Day: LocalCalendar.DateOf(session.StartedAt, zone), Seconds: Seconds(session, now)))
            .ToList();
        var totalSeconds = played.Sum(session => session.Seconds);
        var lastPlayed = sessions.Max(session => session.StartedAt);

        var startYear = played.Min(session => session.Day.Year);
        var endYear = Math.Max(LocalCalendar.DateOf(now, zone).Year, played.Max(session => session.Day.Year));
        var years = Enumerable.Range(startYear, endYear - startYear + 1)
            .Select(year => new GameYearStatDto(
                year,
                played.Where(session => session.Day.Year == year).Sum(session => session.Seconds)))
            .ToArray();
        var months = Enumerable.Range(startYear, endYear - startYear + 1)
            .SelectMany(year => Enumerable.Range(1, 12).Select(month => (year, month)))
            .Select(pair => new GameMonthStatDto(
                pair.year,
                pair.month,
                played.Where(session => session.Day.Year == pair.year && session.Day.Month == pair.month)
                    .Sum(session => session.Seconds)))
            .ToArray();

        return new GameStatsResponseDto(totalSeconds, sessions.Count, lastPlayed, years, months);
    }

    public async Task<AccountStatsPeriodDto> ComputeStats(
        long userId, long? accountId, DateTimeOffset from, DateTimeOffset to,
        string? platform, string? title, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var allSessions = await LoadStatsRows(userId, accountId, title, from, to, ct);
        var platforms = allSessions
            .GroupBy(session => session.PlatformKey)
            .Select(g => new AccountPlatformStatDto(
                g.Key, g.First().PlatformName, g.Sum(session => Seconds(session, now)), null))
            .OrderBy(platform => platform.PlatformKey)
            .ToList();

        var normalizedPlatform = string.IsNullOrWhiteSpace(platform)
            ? null
            : platform.Trim().ToLowerInvariant();
        var sessions = normalizedPlatform is null
            ? allSessions
            : allSessions.Where(session => session.PlatformKey == normalizedPlatform).ToList();
        var months = BuildMonthStats(from, to, sessions, now, zone);
        if (sessions.Count == 0)
            return new AccountStatsPeriodDto(0, 0, null, platforms, [], months);

        var totalSeconds = sessions.Sum(session => Seconds(session, now));

        // Group on the raw id: re-parsing an encoded id could read an all-digit hash as a raw id.
        var totals = sessions
            .GroupBy(session => session.GameId)
            .ToDictionary(group => group.Key, group => group.Sum(session => Seconds(session, now)));
        var titles = sessions
            .GroupBy(session => session.GameId)
            .ToDictionary(group => group.Key, group => group.First().GameTitle);
        var edgeRows = await db.GameConnections
            .AsNoTracking()
            .Where(edge => edge.UserId == userId)
            .Select(edge => new { edge.GameId, edge.ConnectedGameId })
            .ToListAsync(ct);
        var merged = games.MergeGameTotals(
            totals,
            edgeRows.Select(edge => (edge.GameId, edge.ConnectedGameId)).ToList());
        var topGames = merged
            .Select(item => new AccountTopGameDto(
                GameId.Encode(item.Key),
                titles.GetValueOrDefault(item.Key) ?? "Unknown game",
                item.Value))
            .OrderByDescending(game => game.TotalSeconds)
            .Take(5)
            .ToList();
        var topGame = topGames.FirstOrDefault()?.Title;

        return new AccountStatsPeriodDto(
            totalSeconds, sessions.Count, topGame, platforms, topGames, months);
    }

    /// <summary>
    /// Play time per day of the client's calendar in [from, to). Unlike the period stats, a session is split across
    /// every day it overlaps, and a running session counts up to now, so no day can hold more than its own length.
    /// </summary>
    public async Task<List<AccountDayStatDto>> ComputeDailyStats(
        long userId, long? accountId, DateTimeOffset from, DateTimeOffset to,
        string? platform, string? title, int ignoreShortMinutes, TimeZoneInfo zone, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await BuildSessionQuery(
                userId, accountId, title, null, platform, null, null, excludeIgnored: true)
            .Where(row => row.Session.StartedAt < to && (row.Session.EndedAt ?? now) > from)
            .OrderBy(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);

        return BuildDayStats(from, to, rows.Select(MapSession).ToList(), now, zone);
    }

    private IQueryable<SessionQueryRow> BuildSessionQuery(
        long userId, long? accountId, string? title, IReadOnlyCollection<long>? gameIds,
        string? platform, DateTimeOffset? from, DateTimeOffset? to, bool excludeIgnored)
    {
        var query =
            from row in db.ClassifiedPlaySessions()
            join p in db.Platforms on row.Session.PlatformId equals p.Id
            join g in db.Games on row.Session.GameId equals g.Id
            where row.Session.UserId == userId
            select new SessionQueryRow { Session = row.Session, Game = g, Platform = p, Ignored = row.Ignored };

        if (accountId.HasValue)
            query = query.Where(row => row.Session.IntegrationId == accountId.Value);

        if (!string.IsNullOrWhiteSpace(title))
        {
            var pattern = SearchText.ContainsPattern(title);
            query = query.Where(row => EF.Functions.ILike(row.Game.Title, pattern, SearchText.EscapeCharacter));
        }

        if (gameIds is { Count: > 0 })
            query = query.Where(row => gameIds.Contains(row.Game.Id));

        if (!string.IsNullOrWhiteSpace(platform))
        {
            var platformKey = platform.Trim().ToLowerInvariant();
            query = query.Where(row => row.Platform.Key == platformKey);
        }

        if (from.HasValue)
            query = query.Where(row => row.Session.StartedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(row => row.Session.StartedAt < to.Value);

        if (excludeIgnored)
            query = query.Where(row => !row.Ignored);

        return query;
    }

    private static readonly Expression<Func<SessionQueryRow, SessionDbRow>> ToSessionDbRow =
        row => new SessionDbRow
        {
            SessionId = row.Session.Id,
            StartedAt = row.Session.StartedAt,
            EndedAt = row.Session.EndedAt,
            GameId = row.Game.Id,
            GameTitle = row.Game.Title,
            PlatformKey = row.Platform.Key,
            PlatformName = row.Platform.Name,
            CapsuleUrl = row.Game.CapsuleUrl,
            Ignored = row.Ignored,
        };

    private static AccountSessionDto MapSession(SessionDbRow row)
    {
        return new AccountSessionDto(
            row.SessionId,
            row.StartedAt,
            row.EndedAt,
            GameId.Encode(row.GameId),
            row.PlatformKey,
            row.PlatformName,
            row.GameTitle,
            row.CapsuleUrl,
            row.Ignored,
            null,
            null);
    }

    /// <summary>A running session counts up to now.</summary>
    private static double Seconds(SessionDbRow session, DateTimeOffset now) =>
        ((session.EndedAt ?? now) - session.StartedAt).TotalSeconds;

    internal static List<AccountDayStatDto> BuildDayStats(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<AccountSessionDto> sessions,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var seconds = new Dictionary<DateOnly, double>();
        var counts = new Dictionary<DateOnly, int>();
        var platforms = new Dictionary<DateOnly, Dictionary<string, (string Name, double Seconds)>>();
        foreach (var session in sessions)
        {
            var start = session.StartedAt > from ? session.StartedAt : from;
            var end = session.EndedAt ?? now;
            if (end > to) end = to;
            for (var day = LocalCalendar.DateOf(start, zone); ; day = day.AddDays(1))
            {
                var dayStart = LocalCalendar.StartOf(day, zone);
                if (dayStart >= end) break;
                var dayEnd = LocalCalendar.StartOf(day.AddDays(1), zone);
                var sliceStart = start > dayStart ? start : dayStart;
                var sliceEnd = end < dayEnd ? end : dayEnd;
                if (sliceEnd <= sliceStart) continue;
                var slice = (sliceEnd - sliceStart).TotalSeconds;
                seconds[day] = seconds.GetValueOrDefault(day) + slice;
                counts[day] = counts.GetValueOrDefault(day) + 1;
                if (!platforms.TryGetValue(day, out var byPlatform))
                    platforms[day] = byPlatform = [];
                var current = byPlatform.GetValueOrDefault(session.PlatformKey, (session.PlatformName, 0));
                byPlatform[session.PlatformKey] = (current.Name, current.Seconds + slice);
            }
        }

        var days = new List<AccountDayStatDto>();
        for (var cursor = LocalCalendar.DateOf(from, zone); LocalCalendar.StartOf(cursor, zone) < to; cursor = cursor.AddDays(1))
        {
            days.Add(new AccountDayStatDto(
                cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                seconds.GetValueOrDefault(cursor),
                counts.GetValueOrDefault(cursor),
                platforms.TryGetValue(cursor, out var dayPlatforms)
                    ? dayPlatforms
                        .OrderBy(item => item.Key, StringComparer.Ordinal)
                        .Select(item => new AccountDayPlatformDto(item.Key, item.Value.Name, item.Value.Seconds))
                        .ToList()
                    : []));
        }
        return days;
    }

    private static List<AccountMonthStatDto> BuildMonthStats(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<SessionDbRow> sessions,
        DateTimeOffset now,
        TimeZoneInfo zone)
    {
        var totals = sessions
            .GroupBy(session =>
            {
                var day = LocalCalendar.DateOf(session.StartedAt, zone);
                return (day.Year, day.Month);
            })
            .ToDictionary(group => group.Key, group => group.Sum(session => Seconds(session, now)));
        var first = LocalCalendar.DateOf(from, zone);
        var months = new List<AccountMonthStatDto>();
        for (var cursor = new DateOnly(first.Year, first.Month, 1);
             LocalCalendar.StartOf(cursor, zone) < to;
             cursor = cursor.AddMonths(1))
        {
            months.Add(new AccountMonthStatDto(
                cursor.Year,
                cursor.Month,
                totals.GetValueOrDefault((cursor.Year, cursor.Month))));
        }
        return months;
    }

}

public sealed record SessionQueryResult(int Total, int Limit, int Offset, List<AccountSessionDto> Sessions);

internal sealed class SessionDbRow
{
    public long SessionId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long GameId { get; set; }
    public string GameTitle { get; set; } = string.Empty;
    public string PlatformKey { get; set; } = string.Empty;
    public string PlatformName { get; set; } = string.Empty;
    public string? CapsuleUrl { get; set; }
    public bool Ignored { get; set; }
}

internal sealed class SessionQueryRow
{
    public required PlaySession Session { get; init; }
    public required Game Game { get; init; }
    public required Platform Platform { get; init; }
    public bool Ignored { get; init; }
}
