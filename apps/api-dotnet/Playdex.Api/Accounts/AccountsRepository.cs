using System.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Common;
using Playdex.Api.Data;

namespace Playdex.Api.Accounts;

public interface IAccountsRepository
{
    Task<List<AccountSummaryDto>> ListAccounts(long userId, CancellationToken ct);
    Task<SessionQueryResult> ListSessions(long userId, long? accountId, string? title, long? gameId, DateTimeOffset? from, DateTimeOffset? to, string sort, string direction, int limit, int offset, int ignoreShortMinutes, CancellationToken ct);
    Task<List<AccountSessionDto>> ListSessionsToday(long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct);
    Task<AccountSessionDto?> LoadCurrentSession(long userId, long? accountId, CancellationToken ct);
    Task<long> DeleteSession(long sessionId, long userId, CancellationToken ct);
    Task<List<AccountSessionDto>> ListSessionsForStats(long userId, long? accountId, DateTimeOffset? from, DateTimeOffset? to, int ignoreShortMinutes, CancellationToken ct);
    Task<AccountStatsPeriodDto> ComputeStats(long userId, long? accountId, DateTimeOffset from, DateTimeOffset to, int ignoreShortMinutes, CancellationToken ct);
}

public sealed class AccountsRepository(NpgsqlDataSource dataSource, PlaydexDbContext db) : IAccountsRepository
{
    public async Task<List<AccountSummaryDto>> ListAccounts(long userId, CancellationToken ct)
    {
        return await db.PlatformCredentials
            .AsNoTracking()
            .Where(pc => pc.UserId == userId)
            .Join(db.Platforms.AsNoTracking(), pc => pc.PlatformId, p => p.Id,
                (pc, p) => new AccountSummaryDto(pc.Id, p.Name))
            .ToListAsync(ct);
    }

    public async Task<SessionQueryResult> ListSessions(
        long userId, long? accountId, string? title, long? gameId,
        DateTimeOffset? from, DateTimeOffset? to,
        string sort, string direction, int limit, int offset,
        int ignoreShortMinutes, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var (sql, parameters) = BuildSessionQuery(userId, accountId, title, gameId, from, to, ignoreShortMinutes);

        var orderBy = sort switch
        {
            "duration" => $"duration_seconds {direction}",
            _ => $"ps.started_at {direction}",
        };

        var countSql = $"SELECT COUNT(*) FROM ({sql}) cnt";
        var total = await conn.ExecuteScalarAsync<int>(countSql, parameters);

        var dataSql = $"{sql} ORDER BY {orderBy} OFFSET @Offset LIMIT @Limit";
        parameters.Add("Offset", offset);
        parameters.Add("Limit", limit);
        var sessions = (await conn.QueryAsync<SessionDbRow>(dataSql, parameters)).ToList();

        return new SessionQueryResult(total, limit, offset, sessions.Select(MapSession).ToList());
    }

    public async Task<List<AccountSessionDto>> ListSessionsToday(
        long userId, long? accountId, int ignoreShortMinutes, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, TimeSpan.Zero);
        var todayEnd = todayStart.AddDays(1);

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var (sql, parameters) = BuildSessionQuery(userId, accountId, null, null, todayStart, todayEnd, ignoreShortMinutes);
        var dataSql = $"{sql} ORDER BY ps.started_at DESC";
        var sessions = (await conn.QueryAsync<SessionDbRow>(dataSql, parameters)).ToList();

        return sessions.Select(MapSession).ToList();
    }

    public async Task<AccountSessionDto?> LoadCurrentSession(
        long userId, long? accountId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var (sql, parameters) = BuildSessionQuery(userId, accountId, null, null, null, null, 0);
        sql += " AND ps.ended_at IS NULL ORDER BY ps.started_at DESC LIMIT 1";
        var session = await conn.QueryFirstOrDefaultAsync<SessionDbRow>(sql, parameters);
        return session is null ? null : MapSession(session);
    }

    public async Task<long> DeleteSession(long sessionId, long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<long>(
            """DELETE FROM play_sessions USING platform_credentials WHERE play_sessions.id = @Id AND platform_credentials.user_id = @UserId AND platform_credentials.id = play_sessions.platform_credential_id RETURNING play_sessions.id""",
            new { Id = sessionId, UserId = userId });
    }

    public async Task<List<AccountSessionDto>> ListSessionsForStats(
        long userId, long? accountId, DateTimeOffset? from, DateTimeOffset? to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var (sql, parameters) = BuildSessionQuery(userId, accountId, null, null, from, to, ignoreShortMinutes);
        sql += " ORDER BY ps.started_at ASC";
        var sessions = (await conn.QueryAsync<SessionDbRow>(sql, parameters)).ToList();
        return sessions.Select(MapSession).ToList();
    }

    public async Task<AccountStatsPeriodDto> ComputeStats(
        long userId, long? accountId, DateTimeOffset from, DateTimeOffset to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var sessions = await ListSessionsForStats(userId, accountId, from, to, ignoreShortMinutes, ct);
        if (sessions.Count == 0)
        {
            return new AccountStatsPeriodDto(0, 0, null, [], []);
        }

        var totalSeconds = sessions.Sum(MapDuration);

        var topGame = sessions
            .GroupBy(s => s.Game)
            .Select(g => new { Game = g.Key, Seconds = g.Sum(MapDuration) })
            .OrderByDescending(g => g.Seconds)
            .FirstOrDefault()?.Game;

        var platforms = sessions
            .GroupBy(s => s.Platform)
            .Select(g => new AccountPlatformStatDto(
                g.Key, g.Sum(MapDuration), null))
            .ToList();

        var topGames = sessions
            .GroupBy(s => s.Game)
            .Select(g => new AccountTopGameDto(
                GameId.Encode(g.First().SessionId),
                g.Key,
                g.Sum(MapDuration)))
            .OrderByDescending(g => g.TotalSeconds)
            .Take(5)
            .ToList();

        return new AccountStatsPeriodDto(totalSeconds, sessions.Count, topGame, platforms, topGames);
    }

    private static (string Sql, DynamicParameters Parameters) BuildSessionQuery(
        long userId, long? accountId, string? title, long? gameId,
        DateTimeOffset? from, DateTimeOffset? to, int ignoreShortMinutes)
    {
        var sql = """
            SELECT ps.id AS SessionId, ps.started_at AS StartedAt, ps.ended_at AS EndedAt,
                   g.id AS GameId, g.title AS GameTitle,
                   gp.name AS Platform, g.capsule_url AS CapsuleUrl,
                   CASE WHEN ps.ended_at IS NOT NULL
                       THEN GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at))))::int
                       ELSE GREATEST(0, FLOOR(EXTRACT(EPOCH FROM (NOW() AT TIME ZONE 'utc' - ps.started_at))))::int
                   END AS DurationSeconds
            FROM play_sessions ps
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            JOIN platforms p ON p.id = pc.platform_id
            JOIN games g ON g.id = ps.game_id
            JOIN platforms gp ON gp.id = g.platform_id
            LEFT JOIN user_settings us ON us.user_id = pc.user_id
            WHERE pc.user_id = @UserId
            """;
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        if (accountId.HasValue)
        {
            sql += " AND pc.id = @AccountId";
            parameters.Add("AccountId", accountId.Value);
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            sql += " AND g.title ILIKE @Title";
            parameters.Add("Title", $"%{title}%");
        }

        if (gameId.HasValue)
        {
            sql += " AND g.id = @GameId";
            parameters.Add("GameId", gameId.Value);
        }

        if (from.HasValue)
        {
            sql += " AND ps.started_at >= @From";
            parameters.Add("From", from.Value);
        }

        if (to.HasValue)
        {
            sql += " AND ps.started_at < @To";
            parameters.Add("To", to.Value);
        }

        if (ignoreShortMinutes > 0)
        {
            sql += """
                 AND NOT (COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                     AND ps.ended_at IS NOT NULL
                     AND EXTRACT(EPOCH FROM (ps.ended_at - ps.started_at)) < COALESCE(us.ignore_short_sessions_minutes, 0) * 60)
                """;
        }

        return (sql, parameters);
    }

    private static AccountSessionDto MapSession(SessionDbRow r) => new(
        r.SessionId, r.StartedAt, r.EndedAt,
        GameId.Encode(r.GameId), r.Platform, r.GameTitle,
        r.CapsuleUrl, false, null, null);

    private static double MapDuration(AccountSessionDto s) =>
        s.EndedAt.HasValue
            ? (s.EndedAt.Value - s.StartedAt).TotalSeconds
            : (DateTimeOffset.UtcNow - s.StartedAt).TotalSeconds;
}

public sealed record SessionQueryResult(int Total, int Limit, int Offset, List<AccountSessionDto> Sessions);

internal sealed class SessionDbRow
{
    public long SessionId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long GameId { get; set; }
    public required string GameTitle { get; set; }
    public required string Platform { get; set; }
    public string? CapsuleUrl { get; set; }
    public int DurationSeconds { get; set; }
}
