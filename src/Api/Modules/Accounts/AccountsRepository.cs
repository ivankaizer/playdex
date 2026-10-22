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

    public async Task<List<AccountSessionDto>> ListSessionsToday(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        var sessions = await BuildSessionQuery(
                userId, accountId, null, null, null, null, null, excludeIgnored: false)
            .Where(row =>
                (row.Session.StartedAt >= todayStart && row.Session.StartedAt < todayEnd) ||
                (row.Session.EndedAt != null && row.Session.EndedAt >= todayStart && row.Session.EndedAt < todayEnd))
            .OrderByDescending(row => row.Session.StartedAt)
            .Take(10)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);

        return sessions.Select(MapSession).ToList();
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

    public Task<PlaySession?> GetOwnedSession(long sessionId, long userId, CancellationToken ct) =>
        db.PlaySessions.SingleOrDefaultAsync(session => session.Id == sessionId && session.UserId == userId, ct);

    public Task SaveChanges(CancellationToken ct) => db.SaveChangesAsync(ct);

    public async Task<List<AccountSessionDto>> ListSessionsForStats(
        long userId, long? accountId, DateTimeOffset? from, DateTimeOffset? to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var sessions = await BuildSessionQuery(
                userId, accountId, null, null, null, from, to, excludeIgnored: true)
            .OrderBy(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);

        return sessions.Select(MapSession).ToList();
    }

    public async Task<GameStatsResponseDto> ComputeGameStats(
        long userId,
        IReadOnlyCollection<long> gameIds,
        CancellationToken ct)
    {
        var sessions = await BuildSessionQuery(
                userId, null, null, gameIds, null, null, null, excludeIgnored: true)
            .OrderBy(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);
        var mapped = sessions.Select(MapSession).ToList();
        if (mapped.Count == 0)
        {
            return new GameStatsResponseDto(0, 0, null, [], []);
        }

        var now = DateTimeOffset.UtcNow;
        var totalSeconds = mapped.Sum(MapDuration);
        var lastPlayed = mapped
            .OrderByDescending(session => session.StartedAt)
            .Select(session => (DateTimeOffset?)session.StartedAt)
            .First();

        var endYear = Math.Max(AllTimeStartYear, now.UtcDateTime.Year);
        var years = Enumerable.Range(AllTimeStartYear, endYear - AllTimeStartYear + 1)
            .Select(year => new GameYearStatDto(
                year,
                mapped.Where(session => session.StartedAt.UtcDateTime.Year == year).Sum(MapDuration)))
            .ToArray();
        var months = Enumerable.Range(AllTimeStartYear, endYear - AllTimeStartYear + 1)
            .SelectMany(year => Enumerable.Range(1, 12).Select(month => (year, month)))
            .Select(pair => new GameMonthStatDto(
                pair.year,
                pair.month,
                mapped.Where(session =>
                    session.StartedAt.UtcDateTime.Year == pair.year &&
                    session.StartedAt.UtcDateTime.Month == pair.month).Sum(MapDuration)))
            .ToArray();

        return new GameStatsResponseDto(totalSeconds, mapped.Count, lastPlayed, years, months);
    }

    private const int AllTimeStartYear = 2025;

    public async Task<AccountStatsPeriodDto> ComputeStats(
        long userId, long? accountId, DateTimeOffset from, DateTimeOffset to,
        string? platform, int ignoreShortMinutes, CancellationToken ct)
    {
        var allSessions = await ListSessionsForStats(userId, accountId, from, to, ignoreShortMinutes, ct);
        var platforms = allSessions
            .GroupBy(session => session.PlatformKey)
            .Select(g => new AccountPlatformStatDto(
                g.Key, g.First().PlatformName, g.Sum(MapDuration)))
            .OrderBy(platform => platform.PlatformKey)
            .ToList();

        var normalizedPlatform = string.IsNullOrWhiteSpace(platform)
            ? null
            : platform.Trim().ToLowerInvariant();
        var sessions = normalizedPlatform is null
            ? allSessions
            : allSessions.Where(session => session.PlatformKey == normalizedPlatform).ToList();
        var months = BuildMonthStats(from, to, sessions);
        if (sessions.Count == 0)
            return new AccountStatsPeriodDto(0, 0, null, platforms, [], months);

        var totalSeconds = sessions.Sum(MapDuration);

        var totals = sessions
            .GroupBy(session => GameId.Parse(session.GameId))
            .ToDictionary(group => group.Key, group => group.Sum(MapDuration));
        var titles = sessions
            .GroupBy(session => GameId.Parse(session.GameId))
            .ToDictionary(group => group.Key, group => group.First().Game);
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
    /// Play time per UTC day in [from, to). Unlike the period stats, a session is split across every day it
    /// overlaps, and a running session counts up to now, so no day can exceed 24 hours.
    /// </summary>
    public async Task<List<AccountDayStatDto>> ComputeDailyStats(
        long userId, long? accountId, DateTimeOffset from, DateTimeOffset to,
        string? platform, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var rows = await BuildSessionQuery(
                userId, accountId, null, null, platform, null, null, excludeIgnored: true)
            .Where(row => row.Session.StartedAt < to && (row.Session.EndedAt ?? now) > from)
            .OrderBy(row => row.Session.StartedAt)
            .Select(ToSessionDbRow)
            .ToListAsync(ct);

        return BuildDayStats(from, to, rows.Select(MapSession).ToList(), now);
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
            var pattern = $"%{title}%";
            query = query.Where(row => EF.Functions.ILike(row.Game.Title, pattern));
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
            row.Ignored);
    }

    private static double MapDuration(AccountSessionDto s) =>
        s.EndedAt.HasValue
            ? (s.EndedAt.Value - s.StartedAt).TotalSeconds
            : (DateTimeOffset.UtcNow - s.StartedAt).TotalSeconds;

    internal static List<AccountDayStatDto> BuildDayStats(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<AccountSessionDto> sessions,
        DateTimeOffset now)
    {
        var firstDay = from.UtcDateTime.Date;
        var seconds = new Dictionary<DateTime, double>();
        var counts = new Dictionary<DateTime, int>();
        var platforms = new Dictionary<DateTime, Dictionary<string, (string Name, double Seconds)>>();
        foreach (var session in sessions)
        {
            var start = session.StartedAt > from ? session.StartedAt : from;
            var end = session.EndedAt ?? now;
            if (end > to) end = to;
            for (var day = start.UtcDateTime.Date; day < end.UtcDateTime; day = day.AddDays(1))
            {
                var dayStart = new DateTimeOffset(day, TimeSpan.Zero);
                var sliceStart = start > dayStart ? start : dayStart;
                var sliceEnd = end < dayStart.AddDays(1) ? end : dayStart.AddDays(1);
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
        for (var cursor = firstDay; cursor < to.UtcDateTime; cursor = cursor.AddDays(1))
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
        IReadOnlyList<AccountSessionDto> sessions)
    {
        var totals = sessions
            .GroupBy(session => (session.StartedAt.UtcDateTime.Year, session.StartedAt.UtcDateTime.Month))
            .ToDictionary(group => group.Key, group => group.Sum(MapDuration));
        var cursor = new DateTimeOffset(from.Year, from.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var months = new List<AccountMonthStatDto>();
        while (cursor < to)
        {
            months.Add(new AccountMonthStatDto(
                cursor.Year,
                cursor.Month,
                totals.GetValueOrDefault((cursor.Year, cursor.Month))));
            cursor = cursor.AddMonths(1);
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
