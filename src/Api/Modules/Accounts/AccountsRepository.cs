using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        var (sql, parameters) = BuildSessionQuery(
            userId, accountId, title, gameIds, platform, from, to, excludeIgnored: false);

        var orderDirection = direction.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
        var orderBy = sort switch
        {
            "duration" => $"\"DurationSeconds\" {orderDirection}",
            _ => $"ps.started_at {orderDirection}",
        };

        var countParams = new List<NpgsqlParameter>(parameters);
        var countSql = "SELECT COUNT(*) AS \"Value\" FROM (" + sql + ") cnt";
        var total = await db.Database.SqlQueryRaw<int>(countSql, [.. countParams])
            .FirstOrDefaultAsync(ct);

        var dataParams = new List<NpgsqlParameter>(parameters)
        {
            new("Offset", offset),
            new("Limit", limit),
        };
        var dataSql = $"{sql} ORDER BY {orderBy} OFFSET @Offset LIMIT @Limit";
        var sessions = await db.Database.SqlQueryRaw<SessionDbRow>(dataSql, [.. dataParams])
            .ToListAsync(ct);

        return new SessionQueryResult(total, limit, offset, sessions.Select(MapSession).ToList());
    }

    public async Task<List<AccountSessionDto>> ListSessionsToday(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        var (sql, parameters) = BuildSessionQuery(
            userId, accountId, null, null, null, null, null, excludeIgnored: false);
        sql += " AND ((ps.started_at >= @TodayStart AND ps.started_at < @TodayEnd)" +
            " OR (ps.ended_at IS NOT NULL AND ps.ended_at >= @TodayStart AND ps.ended_at < @TodayEnd))";
        parameters.Add(new NpgsqlParameter("TodayStart", todayStart));
        parameters.Add(new NpgsqlParameter("TodayEnd", todayEnd));
        var dataSql = $"{sql} ORDER BY ps.started_at DESC LIMIT 10";
        var sessions = await db.Database.SqlQueryRaw<SessionDbRow>(dataSql, [.. parameters])
            .ToListAsync(ct);

        return sessions.Select(MapSession).ToList();
    }

    public async Task<AccountSessionDto?> LoadCurrentSession(
        long userId, long? accountId, CancellationToken ct)
    {
        var (sql, parameters) = BuildSessionQuery(
            userId, accountId, null, null, null, null, null, excludeIgnored: false);
        sql += " AND ps.ended_at IS NULL ORDER BY ps.started_at DESC LIMIT 1";
        var session = await db.Database.SqlQueryRaw<SessionDbRow?>(sql, [.. parameters])
            .FirstOrDefaultAsync(ct);
        return session is null ? null : MapSession(session);
    }

    public async Task<long> DeleteSession(long sessionId, long userId, CancellationToken ct)
    {
        var affected = await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM play_sessions WHERE id = @Id AND user_id = @UserId
            """,
            [new NpgsqlParameter("Id", sessionId), new NpgsqlParameter("UserId", userId)],
            ct);
        return affected == 1 ? sessionId : 0;
    }

    public async Task<List<AccountSessionDto>> ListSessionsForStats(
        long userId, long? accountId, DateTimeOffset? from, DateTimeOffset? to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var (sql, parameters) = BuildSessionQuery(
            userId, accountId, null, null, null, from, to, excludeIgnored: true);
        sql += " ORDER BY ps.started_at ASC";
        var sessions = await db.Database.SqlQueryRaw<SessionDbRow>(sql, [.. parameters])
            .ToListAsync(ct);

        return sessions.Select(MapSession).ToList();
    }

    public async Task<GameStatsResponseDto> ComputeGameStats(
        long userId,
        IReadOnlyCollection<long> gameIds,
        CancellationToken ct)
    {
        var (sql, parameters) = BuildSessionQuery(
            userId, null, null, gameIds, null, null, null, excludeIgnored: true);
        sql += " ORDER BY ps.started_at ASC";
        var sessions = await db.Database.SqlQueryRaw<SessionDbRow>(sql, [.. parameters])
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
                g.Key, g.First().PlatformName, g.Sum(MapDuration), null))
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

    private static (string Sql, List<NpgsqlParameter> Parameters) BuildSessionQuery(
        long userId, long? accountId, string? title, IReadOnlyCollection<long>? gameIds,
        string? platform, DateTimeOffset? from, DateTimeOffset? to, bool excludeIgnored)
    {
        var sql = """
            SELECT ps.id AS "SessionId", ps.started_at AS "StartedAt", ps.ended_at AS "EndedAt",
                   g.id AS "GameId", g.title AS "GameTitle",
                   p.key AS "PlatformKey", p.name AS "PlatformName",
                   g.capsule_url AS "CapsuleUrl",
                   CASE
                       WHEN COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                           AND ps.ended_at IS NOT NULL
                           AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))
                               < COALESCE(us.ignore_short_sessions_minutes, 0) * 60
                       THEN TRUE ELSE FALSE
                   END AS "Ignored",
                   CASE WHEN ps.ended_at IS NOT NULL
                       THEN GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))))::int
                       ELSE GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'utc' - ps.started_at))))::int
                   END AS "DurationSeconds"
            FROM play_sessions ps
            JOIN platforms p ON p.id = ps.platform_id
            JOIN games g ON g.id = ps.game_id
            LEFT JOIN user_settings us ON us.user_id = ps.user_id
            WHERE ps.user_id = @UserId
            """;

        var parameters = new List<NpgsqlParameter>
        {
            new("UserId", userId),
        };

        if (accountId.HasValue)
        {
            sql += " AND ps.integration_id = @AccountId";
            parameters.Add(new NpgsqlParameter("AccountId", accountId.Value));
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            sql += " AND g.title ILIKE @Title";
            parameters.Add(new NpgsqlParameter("Title", $"%{title}%"));
        }

        if (gameIds is { Count: 1 })
        {
            sql += " AND g.id = @GameId";
            parameters.Add(new NpgsqlParameter("GameId", gameIds.First()));
        }

        else if (gameIds is { Count: > 1 })
        {
            sql += " AND g.id = ANY(@GameIds)";
            parameters.Add(new NpgsqlParameter("GameIds", gameIds.ToArray()));
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            sql += " AND p.key = @Platform";
            parameters.Add(new NpgsqlParameter("Platform", platform.Trim().ToLowerInvariant()));
        }

        if (from.HasValue)
        {
            sql += " AND ps.started_at >= @From";
            parameters.Add(new NpgsqlParameter("From", from.Value));
        }

        if (to.HasValue)
        {
            sql += " AND ps.started_at < @To";
            parameters.Add(new NpgsqlParameter("To", to.Value));
        }

        if (excludeIgnored)
        {
            sql += """
                 AND NOT (COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                     AND ps.ended_at IS NOT NULL
                     AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at)) < COALESCE(us.ignore_short_sessions_minutes, 0) * 60)
                """;
        }

        return (sql, parameters);
    }

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

    private static double MapDuration(AccountSessionDto s) =>
        s.EndedAt.HasValue
            ? (s.EndedAt.Value - s.StartedAt).TotalSeconds
            : (DateTimeOffset.UtcNow - s.StartedAt).TotalSeconds;

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
    public int DurationSeconds { get; set; }
}
