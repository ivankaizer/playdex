using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;

namespace Api.Modules.ApiCapture;

public sealed class ApiCaptureRepository(PlaydexDbContext db)
{
    public async Task<ApiIntegrationRow?> LoadCredentialByApiKey(string apiKey, CancellationToken ct)
    {
        var credentials = await db.Database.SqlQuery<ApiIntegrationRow>($"""
            SELECT pc.id AS "Id", pc.user_id AS "UserId"
            FROM integrations pc
            WHERE pc.api_key = {apiKey}
              AND pc.type = 'api'
              AND pc.active = true
            """).Take(2).ToListAsync(ct);
        return credentials.Count == 1 ? credentials[0] : null;
    }

    public Task<List<ApiCapturePlatformDto>> ListPcPlatforms(CancellationToken ct) =>
        db.Database.SqlQuery<ApiCapturePlatformDto>($"""
            WITH RECURSIVE pc_platforms AS (
                SELECT id, key, name, parent_id, 0 AS depth
                FROM platforms
                WHERE key = 'pc'

                UNION ALL

                SELECT child.id, child.key, child.name, child.parent_id, parent.depth + 1
                FROM platforms child
                JOIN pc_platforms parent ON child.parent_id = parent.id
            )
            SELECT key AS "Key", name AS "Name"
            FROM pc_platforms
            ORDER BY CASE WHEN key = 'pc' THEN 0 ELSE 1 END, name, key
            """).ToListAsync(ct);

    public async Task<long> InsertCompletePlaySession(
        ApiIntegrationRow integration, long gameId, long platformId, DateTimeOffset startedAt, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await db.Database.SqlQuery<long>($"""
            INSERT INTO play_sessions (game_id, user_id, platform_id, capture_method, integration_id, started_at, ended_at, created_at, updated_at)
            VALUES ({gameId}, {integration.UserId}, {platformId}, 'api', {integration.Id}, {startedAt}, {endedAt}, {now}, {now})
            RETURNING id
            """).ToListAsync(ct);
        return ids.Single();
    }

    public async Task<long> InsertActivePlaySession(
        ApiIntegrationRow integration, long gameId, long platformId, DateTimeOffset startedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var ids = await db.Database.SqlQuery<long>($"""
            INSERT INTO play_sessions (game_id, user_id, platform_id, capture_method, integration_id, started_at, created_at, updated_at)
            VALUES ({gameId}, {integration.UserId}, {platformId}, 'api', {integration.Id}, {startedAt}, {now}, {now})
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
            WHERE integration_id = {credentialId} AND ended_at IS NULL
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
            WHERE integration_id = {credentialId}
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
            WHERE integration_id = {credentialId}
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
            WHERE integration_id = {credentialId} AND ended_at IS NULL
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
            FROM integrations i
            WHERE ps.integration_id = i.id
              AND i.type = 'api'
              AND ps.ended_at IS NULL
              AND ps.updated_at < {timeoutBefore}
            """, ct);
    }
}

public sealed class ApiIntegrationRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
}

public sealed class ActiveSessionRow
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
