using Microsoft.EntityFrameworkCore;
using Npgsql;
using Api.Shared.Common;
using Api.Shared.Persistence;
using Api.Modules.Games;

namespace Api.Modules.Accounts;

public sealed class AccountsRepository(PlaydexDbContext db, IGames games)
{
    public async Task<List<AccountSummaryDto>> ListAccounts(long userId, CancellationToken ct)
    {
        return await db.PlatformCredentials
            .AsNoTracking()
            .Where(pc => pc.UserId == userId)
            .Join(db.Platforms.AsNoTracking(), pc => pc.PlatformId, p => p.Id,
                (pc, p) => new AccountSummaryDto(pc.Id, p.Key))
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
        DateTimeOffset? from, DateTimeOffset? to,
        string sort, string direction, int limit, int offset,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var (sql, parameters) = BuildSessionQuery(userId, accountId, title, gameIds, from, to, excludeIgnored: false);

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

        var (sql, parameters) = BuildSessionQuery(userId, accountId, null, null, null, null, excludeIgnored: false);
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
        var (sql, parameters) = BuildSessionQuery(userId, accountId, null, null, null, null, excludeIgnored: false);
        sql += " AND ps.ended_at IS NULL ORDER BY ps.started_at DESC LIMIT 1";
        var session = await db.Database.SqlQueryRaw<SessionDbRow?>(sql, [.. parameters])
            .FirstOrDefaultAsync(ct);
        return session is null ? null : MapSession(session);
    }

    public async Task<long> DeleteSession(long sessionId, long userId, CancellationToken ct)
    {
        var affected = await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM play_sessions USING platform_credentials WHERE play_sessions.id = @Id AND platform_credentials.user_id = @UserId AND platform_credentials.id = play_sessions.platform_credential_id
            """,
            [new NpgsqlParameter("Id", sessionId), new NpgsqlParameter("UserId", userId)],
            ct);
        return affected == 1 ? sessionId : 0;
    }

    public async Task<List<AccountSessionDto>> ListSessionsForStats(
        long userId, long? accountId, DateTimeOffset? from, DateTimeOffset? to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var (sql, parameters) = BuildSessionQuery(userId, accountId, null, null, from, to, excludeIgnored: true);
        sql += " ORDER BY ps.started_at ASC";
        var sessions = await db.Database.SqlQueryRaw<SessionDbRow>(sql, [.. parameters])
            .ToListAsync(ct);

        return sessions.Select(MapSession).ToList();
    }

    public async Task<AccountStatsPeriodDto> ComputeStats(
        long userId, long? accountId, DateTimeOffset from, DateTimeOffset to,
        int ignoreShortMinutes, CancellationToken ct)
    {
        var sessions = await ListSessionsForStats(userId, accountId, from, to, ignoreShortMinutes, ct);
        if (sessions.Count == 0)
            return new AccountStatsPeriodDto(0, 0, null, [], []);

        var totalSeconds = sessions.Sum(MapDuration);

        var platforms = sessions
            .GroupBy(s => s.MappedPlatform ?? s.Platform)
            .Select(g => new AccountPlatformStatDto(
                g.Key, g.Sum(MapDuration), null))
            .OrderBy(platform => platform.Platform)
            .ToList();

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

        return new AccountStatsPeriodDto(totalSeconds, sessions.Count, topGame, platforms, topGames);
    }

    private static (string Sql, List<NpgsqlParameter> Parameters) BuildSessionQuery(
        long userId, long? accountId, string? title, IReadOnlyCollection<long>? gameIds,
        DateTimeOffset? from, DateTimeOffset? to, bool excludeIgnored)
    {
        var sql = """
            SELECT ps.id AS "SessionId", ps.started_at AS "StartedAt", ps.ended_at AS "EndedAt",
                   g.id AS "GameId", g.title AS "GameTitle",
                   p.key AS "Platform", g.capsule_url AS "CapsuleUrl",
                   gp.key AS "GamePlatform", g.canonical_key AS "GameCanonicalKey",
                   mg.id AS "MappedGameId", mg.title AS "MappedGameTitle",
                   mp.key AS "MappedGamePlatform", mg.capsule_url AS "MappedCapsuleUrl",
                   mg.canonical_key AS "MappedCanonicalKey",
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
            JOIN platform_credentials pc ON pc.id = ps.platform_credential_id
            JOIN platforms p ON p.id = pc.platform_id
            JOIN games g ON g.id = ps.game_id
            JOIN platforms gp ON gp.id = g.platform_id
            LEFT JOIN user_settings us ON us.user_id = pc.user_id
            LEFT JOIN manual_game_inputs mgi
                ON p.key = 'manual'
                AND mgi.user_id = pc.user_id
                AND mgi.canonical_title = TRIM(regexp_replace(lower(g.title), '[^a-z0-9]+', ' ', 'g'))
            LEFT JOIN games mg ON mg.id = mgi.mapped_game_id
            LEFT JOIN platforms mp ON mp.id = mg.platform_id
            WHERE pc.user_id = @UserId
            """;

        var parameters = new List<NpgsqlParameter>
        {
            new("UserId", userId),
        };

        if (accountId.HasValue)
        {
            sql += " AND pc.id = @AccountId";
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
        var isManual = row.Platform == "manual";
        var hasMappedGame = isManual && row.MappedGameId.HasValue && row.MappedGameTitle is not null;
        var usesNonManualGame = isManual && row.GamePlatform != "manual";
        var gameId = hasMappedGame ? row.MappedGameId!.Value : row.GameId;
        var game = hasMappedGame ? row.MappedGameTitle! : row.GameTitle;
        var capsule = hasMappedGame ? row.MappedCapsuleUrl ?? row.CapsuleUrl : row.CapsuleUrl;
        var mappedPlatform = hasMappedGame
            ? row.MappedGamePlatform
            : usesNonManualGame ? row.GamePlatform : null;
        var canonicalKey = hasMappedGame ? row.MappedCanonicalKey : row.GameCanonicalKey;
        var mappedSource = mappedPlatform is null
            ? null
            : canonicalKey?.StartsWith("igdb:", StringComparison.Ordinal) == true ? "igdb" : "local";

        return new AccountSessionDto(
            row.SessionId,
            row.StartedAt,
            row.EndedAt,
            GameId.Encode(gameId),
            row.Platform,
            game,
            capsule,
            row.Ignored,
            mappedPlatform,
            mappedSource);
    }

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
    public string GameTitle { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? CapsuleUrl { get; set; }
    public string GamePlatform { get; set; } = string.Empty;
    public string GameCanonicalKey { get; set; } = string.Empty;
    public long? MappedGameId { get; set; }
    public string? MappedGameTitle { get; set; }
    public string? MappedGamePlatform { get; set; }
    public string? MappedCapsuleUrl { get; set; }
    public string? MappedCanonicalKey { get; set; }
    public bool Ignored { get; set; }
    public int DurationSeconds { get; set; }
}
