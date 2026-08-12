using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Playnite;

public sealed class PlayniteRepository(PlaydexDbContext db)
{
    public async Task<PlayniteCredentialRow?> LoadCredentialByApiKey(string apiKey, CancellationToken ct)
    {
        var credentials = await db.Database.SqlQuery<PlayniteCredentialRow>($"""
            SELECT pc.id AS "Id"
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            WHERE pc.api_key = {apiKey}
              AND p.key = 'playnite'
              AND pc.active = true
            """).Take(2).ToListAsync(ct);
        return credentials.Count == 1 ? credentials[0] : null;
    }

    public async Task<long> InsertCompletePlaySession(
        long credentialId, long gameId, DateTimeOffset startedAt, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await db.Database.SqlQuery<long>($"""
            INSERT INTO play_sessions (platform_credential_id, game_id, started_at, ended_at, created_at, updated_at)
            VALUES ({credentialId}, {gameId}, {startedAt}, {endedAt}, {now}, {now})
            RETURNING id
            """).ToListAsync(ct);
        return ids.Single();
    }

    public async Task<long> InsertActivePlaySession(
        long credentialId, long gameId, DateTimeOffset startedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await db.Database.SqlQuery<long>($"""
            INSERT INTO play_sessions (platform_credential_id, game_id, started_at, created_at, updated_at)
            VALUES ({credentialId}, {gameId}, {startedAt}, {now}, {now})
            RETURNING id
            """).ToListAsync(ct);
        return ids.Single();
    }

    public async Task<int> EndActiveSessionsForCredential(
        long credentialId, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE play_sessions
            SET ended_at = {endedAt}, updated_at = {now}
            WHERE platform_credential_id = {credentialId} AND ended_at IS NULL
            """, ct);
    }

    public async Task<int> EndActiveSessionsForCredentialExceptGame(
        long credentialId, long gameId, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE play_sessions
            SET ended_at = {endedAt}, updated_at = {now}
            WHERE platform_credential_id = {credentialId}
              AND ended_at IS NULL
              AND game_id <> {gameId}
            """, ct);
    }

    public async Task<int> EndTimedOutActiveSessionsForCredential(
        long credentialId, DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes,
        CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE play_sessions
            SET ended_at = LEAST({endedAt}, updated_at + ({timeoutMinutes} * INTERVAL '1 minute')),
                updated_at = NOW()
            WHERE platform_credential_id = {credentialId}
              AND ended_at IS NULL
              AND updated_at < {timeoutBefore}
            """, ct);
    }

    public async Task<ActiveSessionRow?> FetchActiveSessionForCredential(
        long credentialId,
        CancellationToken ct)
    {
        return await db.Database.SqlQuery<ActiveSessionRow?>($"""
            SELECT
                id AS "Id",
                game_id AS "GameId",
                started_at AS "StartedAt",
                updated_at AS "UpdatedAt"
            FROM play_sessions
            WHERE platform_credential_id = {credentialId} AND ended_at IS NULL
            ORDER BY started_at DESC
            LIMIT 1
            """).FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TouchActiveSession(
        long sessionId, DateTimeOffset touchedAt,
        CancellationToken ct)
    {
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE play_sessions SET updated_at = {touchedAt} WHERE id = {sessionId} AND ended_at IS NULL
            """, ct);
        return affected > 0;
    }

    public async Task<int> EndTimedOutActiveSessions(
        DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes,
        CancellationToken ct)
    {
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE play_sessions ps
            SET ended_at = LEAST({endedAt}, ps.updated_at + ({timeoutMinutes} * INTERVAL '1 minute')),
                updated_at = NOW()
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            WHERE ps.platform_credential_id = pc.id
              AND p.key = 'playnite'
              AND ps.ended_at IS NULL
              AND ps.updated_at < {timeoutBefore}
            """, ct);
    }
}

public sealed class PlayniteCredentialRow
{
    public long Id { get; set; }
}

public sealed class ActiveSessionRow
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
