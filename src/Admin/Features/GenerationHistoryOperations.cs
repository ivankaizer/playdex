using System.Security.Claims;
using Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Admin.Features;

public sealed record GenerationSummary(Guid Id, string Email, DateTimeOffset CreatedAt, string Provider,
    string Model, string Status, string? InputPreview, bool HasImage);
public sealed record GenerationSearchResult(IReadOnlyList<GenerationSummary> Items, int Total, int Page);
public sealed record GenerationDetail(string Email, AiGeneration Generation);
public sealed record GenerationImage(byte[] Bytes, string ContentType);

public interface IGenerationHistoryOperations
{
    Task<GenerationSearchResult> Search(ClaimsPrincipal principal, string query, string? status, int page, DateTimeOffset? since = null, DateTimeOffset? until = null, CancellationToken ct = default);
    Task<GenerationDetail?> GetDetailsAsync(ClaimsPrincipal principal, Guid id, CancellationToken ct = default);
    Task<GenerationImage?> GetImage(ClaimsPrincipal principal, Guid id, CancellationToken ct = default);
    Task<bool> Delete(ClaimsPrincipal principal, Guid id, CancellationToken ct = default);
}

public sealed class GenerationHistoryOperations(IDbContextFactory<PlaydexDbContext> factory,
    AdminOperationGuard guard) : IGenerationHistoryOperations
{
    public async Task<GenerationSearchResult> Search(ClaimsPrincipal principal, string query, string? status,
        int page, DateTimeOffset? since = null, DateTimeOffset? until = null, CancellationToken ct = default)
    {
        await DemandAdmin(principal, ct);
        page = Math.Clamp(page, 0, 100_000);
        await using var db = await factory.CreateDbContextAsync(ct);
        var like = $"%{query.Trim()}%";
        var rows = from generation in db.AiGenerations.AsNoTracking()
            join user in db.Users.AsNoTracking() on generation.UserId equals user.Id
            where EF.Functions.ILike(user.Email, like) || EF.Functions.ILike(generation.InputText!, like)
            select new { generation, user.Email };
        if (status == "errors") rows = rows.Where(row => row.generation.Status == "failure" || row.generation.Status == "timeout" || row.generation.Status == "cancelled");
        else if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(row => row.generation.Status == status);
        if (since is not null) rows = rows.Where(row => row.generation.CreatedAt >= since);
        if (until is not null) rows = rows.Where(row => row.generation.CreatedAt <= until);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(row => row.generation.CreatedAt).ThenByDescending(row => row.generation.Id)
            .Skip(page * 25).Take(25)
            .Select(row => new GenerationSummary(row.generation.Id, row.Email, row.generation.CreatedAt,
                row.generation.Provider, row.generation.Model, row.generation.Status,
                row.generation.InputText == null ? null : row.generation.InputText.Substring(0, Math.Min(row.generation.InputText.Length, 100)),
                row.generation.ImageContentType != null)).ToListAsync(ct);
        return new(items, total, page);
    }

    public async Task<GenerationDetail?> GetDetailsAsync(ClaimsPrincipal principal, Guid id, CancellationToken ct = default)
    {
        await DemandAdmin(principal, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await (from generation in db.AiGenerations.AsNoTracking()
            join user in db.Users.AsNoTracking() on generation.UserId equals user.Id
            where generation.Id == id
            select new GenerationDetail(user.Email, new AiGeneration
            {
                Id=generation.Id, UserId=generation.UserId, RequestId=generation.RequestId,
                CreatedAt=generation.CreatedAt, CompletedAt=generation.CompletedAt,
                Provider=generation.Provider, Model=generation.Model, Timezone=generation.Timezone,
                ReferenceDate=generation.ReferenceDate, InputText=generation.InputText,
                ImageContentType=generation.ImageContentType, Status=generation.Status,
                Error=generation.Error, ResultJson=generation.ResultJson, ResponsesJson=generation.ResponsesJson,
            })).SingleOrDefaultAsync(ct);
    }

    public async Task<GenerationImage?> GetImage(ClaimsPrincipal principal, Guid id, CancellationToken ct = default)
    {
        await DemandAdmin(principal, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AiGenerations.AsNoTracking().Where(row => row.Id == id && row.Image != null
            && (row.ImageContentType == "image/png" || row.ImageContentType == "image/jpeg" || row.ImageContentType == "image/webp"))
            .Select(row => new GenerationImage(row.Image!, row.ImageContentType!)).SingleOrDefaultAsync(ct);
    }

    public async Task<bool> Delete(ClaimsPrincipal principal, Guid id, CancellationToken ct = default)
    {
        await DemandAdmin(principal, ct);
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.AiGenerations.Where(row => row.Id == id).ExecuteDeleteAsync(ct) > 0;
    }

    private async Task DemandAdmin(ClaimsPrincipal principal, CancellationToken ct)
    {
        if (principal.Identity?.IsAuthenticated != true || !await guard.IsAdministratorAsync(principal, ct))
            throw new UnauthorizedAccessException();
    }
}
