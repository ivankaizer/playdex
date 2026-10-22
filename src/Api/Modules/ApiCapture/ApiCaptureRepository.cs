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
}

public sealed class ApiIntegrationRow
{
    public long Id { get; set; }
    public long UserId { get; set; }
}
