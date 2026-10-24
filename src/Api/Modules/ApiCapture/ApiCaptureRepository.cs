using Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Modules.ApiCapture;

public sealed class ApiCaptureRepository(PlaydexDbContext db)
{
    public async Task<ApiIntegrationRow?> LoadCredentialByApiKey(string apiKey, CancellationToken ct)
    {
        var hash = PlaydexAppKeys.Hash(apiKey);
        return await db.Integrations
            .AsNoTracking()
            .Where(pc => pc.ApiKey == hash && pc.Type == IntegrationType.Api && pc.Active)
            .Select(pc => new ApiIntegrationRow { Id = pc.Id, UserId = pc.UserId })
            .FirstOrDefaultAsync(ct);
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
}

public sealed class ApiIntegrationRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
}
