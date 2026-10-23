using Api.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.Launcher;

public sealed class LauncherRepository(PlaydexDbContext db)
{
    /// <summary>
    /// All of the user's sessions, except those the "ignore short sessions" setting hides (as /stats does). The
    /// setting is compared as an interval, capped at a day, so a stored value of any size cannot overflow.
    /// </summary>
    public async Task<List<LauncherSessionRow>> ListSessions(long userId, CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<SessionDbRow>($"""
            SELECT ps.id AS "SessionId", ps.game_id AS "GameId", p.key AS "PlatformKey", p.name AS "PlatformName",
                   ps.started_at AS "StartedAt", ps.ended_at AS "EndedAt"
            FROM play_sessions ps
            JOIN platforms p ON p.id = ps.platform_id
            LEFT JOIN user_settings us ON us.user_id = ps.user_id
            WHERE ps.user_id = {userId}
              AND NOT (COALESCE(us.ignore_short_sessions_minutes, 0) > 0
                  AND ps.ended_at IS NOT NULL
                  AND ps.ended_at - ps.started_at < make_interval(
                      mins => LEAST(COALESCE(us.ignore_short_sessions_minutes, 0), {SessionLimits.MaxDurationMinutes})))
            """).ToListAsync(ct);
        return rows.Select(row => new LauncherSessionRow(row.SessionId, row.GameId, row.PlatformKey, row.PlatformName, row.StartedAt, row.EndedAt)).ToList();
    }

    public Task<bool> HasActiveSteamIntegration(long userId, CancellationToken ct) =>
        db.Integrations.AnyAsync(row => row.UserId == userId && row.Type == IntegrationType.Steam && row.Active, ct);

    public async Task<Dictionary<long, LauncherGameRow>> LoadGames(long[] gameIds, CancellationToken ct)
    {
        if (gameIds.Length == 0)
        {
            return [];
        }
        var rows = await db.Database.SqlQuery<GameDbRow>($"""
            SELECT g.id AS "Id", g.title AS "Title", g.capsule_url AS "CapsuleUrl"
            FROM games g
            WHERE g.id = ANY({gameIds})
            """).ToListAsync(ct);
        return rows.ToDictionary(row => row.Id, row => new LauncherGameRow(row.Id, row.Title, row.CapsuleUrl));
    }

    public async Task<List<LauncherIdentityRow>> LoadIdentities(long[] gameIds, CancellationToken ct)
    {
        if (gameIds.Length == 0)
        {
            return [];
        }
        var rows = await db.Database.SqlQuery<IdentityDbRow>($"""
            SELECT gei.game_id AS "GameId", gei.type::text AS "Type", gei.external_id AS "ExternalId"
            FROM game_external_identities gei
            WHERE gei.game_id = ANY({gameIds})
            """).ToListAsync(ct);
        return rows.Select(row => new LauncherIdentityRow(row.GameId, row.Type, row.ExternalId)).ToList();
    }

    private sealed class SessionDbRow
    {
        public long SessionId { get; set; }
        public long GameId { get; set; }
        public string PlatformKey { get; set; } = "";
        public string PlatformName { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
    }

    private sealed class GameDbRow
    {
        public long Id { get; set; }
        public string Title { get; set; } = "";
        public string? CapsuleUrl { get; set; }
    }

    private sealed class IdentityDbRow
    {
        public long GameId { get; set; }
        public string Type { get; set; } = "";
        public string ExternalId { get; set; } = "";
    }
}
