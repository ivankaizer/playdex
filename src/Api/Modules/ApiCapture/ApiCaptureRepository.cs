using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.ApiCapture;

public sealed class ApiCaptureRepository(PlaydexDbContext db)
{
    public async Task<ApiIntegrationRow?> LoadCredentialByApiKey(string apiKey, CancellationToken ct)
    {
        var credentials = await db.Integrations
            .AsNoTracking()
            .Where(pc => pc.ApiKey == apiKey && pc.Type == IntegrationType.Api && pc.Active)
            .Select(pc => new ApiIntegrationRow { Id = pc.Id, UserId = pc.UserId })
            .Take(2)
            .ToListAsync(ct);
        return credentials.Count == 1 ? credentials[0] : null;
    }

    public async Task<List<ApiCapturePlatformDto>> ListPcPlatforms(CancellationToken ct)
    {
        // The platform tree is tiny, so walk it in memory instead of using a recursive CTE.
        var tree = await db.Platforms
            .AsNoTracking()
            .Select(p => new { p.Id, p.Key, p.ParentId })
            .ToListAsync(ct);
        var ids = tree.Where(p => p.Key == "pc").Select(p => p.Id).ToHashSet();
        var frontier = ids.ToList();
        while (frontier.Count > 0)
        {
            frontier = tree
                .Where(p => p.ParentId is { } parentId && frontier.Contains(parentId) && ids.Add(p.Id))
                .Select(p => p.Id)
                .ToList();
        }

        return await db.Platforms
            .AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .OrderBy(p => p.Key == "pc" ? 0 : 1)
            .ThenBy(p => p.Name)
            .ThenBy(p => p.Key)
            .Select(p => new ApiCapturePlatformDto(p.Key, p.Name))
            .ToListAsync(ct);
    }

    public Task<long> InsertCompletePlaySession(
        ApiIntegrationRow integration, long gameId, long platformId, DateTimeOffset startedAt, DateTimeOffset endedAt,
        CancellationToken ct) =>
        InsertPlaySession(integration, gameId, platformId, startedAt, endedAt, ct);

    public Task<long> InsertActivePlaySession(
        ApiIntegrationRow integration, long gameId, long platformId, DateTimeOffset startedAt,
        CancellationToken ct) =>
        InsertPlaySession(integration, gameId, platformId, startedAt, null, ct);

    private async Task<long> InsertPlaySession(
        ApiIntegrationRow integration, long gameId, long platformId, DateTimeOffset startedAt, DateTimeOffset? endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new PlaySession
        {
            GameId = gameId,
            UserId = integration.UserId,
            PlatformId = platformId,
            CaptureMethod = CaptureMethod.Api,
            IntegrationId = integration.Id,
            StartedAt = startedAt,
            EndedAt = endedAt,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    /// <summary>Holds recordings (POST /capture/sessions) of one API key to one at a time until the transaction ends.</summary>
    public Task LockRecordings(long credentialId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({"capture-record:" + credentialId}, 0))", ct);

    /// <summary>
    /// When the sessions the API key has of a game on a platform ran that overlap <paramref name="from"/>..<paramref name="to"/>;
    /// an ongoing session has no end.
    /// </summary>
    public async Task<List<(DateTimeOffset StartedAt, DateTimeOffset? EndedAt)>> ListSessionRanges(
        long credentialId, long gameId, long platformId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var rows = await db.PlaySessions
            .AsNoTracking()
            .Where(ps => ps.IntegrationId == credentialId && ps.GameId == gameId && ps.PlatformId == platformId
                && ps.StartedAt < to && (ps.EndedAt == null || ps.EndedAt > from))
            .Select(ps => new { ps.StartedAt, ps.EndedAt })
            .ToListAsync(ct);
        return rows.ConvertAll(row => (row.StartedAt, row.EndedAt));
    }

    public Task<int> EndActiveSessionsForCredential(
        long credentialId, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return db.PlaySessions
            .Where(ps => ps.IntegrationId == credentialId && ps.EndedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(ps => ps.EndedAt, endedAt)
                .SetProperty(ps => ps.UpdatedAt, now), ct);
    }

    // UPDATE ... FROM ... RETURNING has no ExecuteUpdate equivalent, so this stays raw.
    public async Task<List<TimedOutSessionRow>> EndTimedOutActiveSessions(
        DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes,
        CancellationToken ct)
    {
        return await db.Database.SqlQuery<TimedOutSessionRow>($"""
            UPDATE play_sessions ps
            SET ended_at = LEAST({endedAt}, ps.updated_at + ({timeoutMinutes} * INTERVAL '1 minute')),
                updated_at = NOW()
            FROM integrations i
            WHERE ps.integration_id = i.id
              AND i.type = 'api'
              AND ps.ended_at IS NULL
              AND ps.updated_at < {timeoutBefore}
            RETURNING
                ps.id AS "Id",
                ps.integration_id AS "IntegrationId",
                ps.game_id AS "GameId",
                ps.started_at AS "StartedAt",
                ps.ended_at AS "EndedAt"
            """).ToListAsync(ct);
    }
}

public sealed class ApiIntegrationRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
}

public sealed class TimedOutSessionRow
{
    public long Id { get; set; }
    public long IntegrationId { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset EndedAt { get; set; }
}
