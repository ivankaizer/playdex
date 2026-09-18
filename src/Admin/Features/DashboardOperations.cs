using System.Security.Claims;
using Admin.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Admin.Features;

public sealed record DashboardCounts(int LiveSessions, int LiveUsers, int SessionsToday, int AiErrors);
public sealed record DashboardPlatform(string Key, string Name);
public sealed record DashboardSession(long Id, string Email, string Game, string? CapsuleUrl,
    string PlatformKey, string Platform, string? AccountId, CaptureMethod Source,
    DateTimeOffset StartedAt, DateTimeOffset? EndedAt, DateTimeOffset? LastPolledAt);
public sealed record DashboardGeneration(Guid Id, string Email, string Model, string Status, DateTimeOffset CreatedAt);
public sealed record DashboardSnapshot(DateTimeOffset UpdatedAt, DashboardCounts Counts,
    IReadOnlyList<DashboardPlatform> Platforms, IReadOnlyList<DashboardSession> LiveSessions,
    IReadOnlyList<DashboardSession> EndedSessions, IReadOnlyList<DashboardGeneration> FailedGenerations);

public interface IDashboardOperations
{
    Task<DashboardSnapshot> Read(ClaimsPrincipal principal, string query, string? platform,
        DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken ct = default);
}

public sealed class DashboardOperations(IDbContextFactory<PlaydexDbContext> factory,
    AdminOperationGuard guard, TimeProvider clock) : IDashboardOperations
{
    public async Task<DashboardSnapshot> Read(ClaimsPrincipal principal, string query, string? platform,
        DateTimeOffset dayStart, DateTimeOffset dayEnd, CancellationToken ct = default)
    {
        if (principal.Identity?.IsAuthenticated != true || !await guard.IsAdministratorAsync(principal, ct))
            throw new UnauthorizedAccessException();
        var now = clock.GetUtcNow();
        var since = now.AddHours(-24);
        await using var db = await factory.CreateDbContextAsync(ct);
        var live = db.PlaySessions.AsNoTracking().Where(row => row.EndedAt == null);
        var failures = db.AiGenerations.AsNoTracking().Where(row => row.Status == "failure"
            || row.Status == "timeout" || row.Status == "cancelled");
        var counts = new DashboardCounts(await live.CountAsync(ct),
            await live.Select(row => row.UserId).Distinct().CountAsync(ct),
            await db.PlaySessions.CountAsync(row => row.StartedAt >= dayStart && row.StartedAt < dayEnd, ct),
            await failures.CountAsync(row => row.CreatedAt >= since && row.CreatedAt <= now, ct));
        var rows = from session in db.PlaySessions.AsNoTracking()
            join user in db.Users.AsNoTracking() on session.UserId equals user.Id
            join game in db.Games.AsNoTracking() on session.GameId equals game.Id
            join playedPlatform in db.Platforms.AsNoTracking() on session.PlatformId equals playedPlatform.Id
            join integration in db.Integrations.AsNoTracking() on session.IntegrationId equals (long?)integration.Id into accounts
            from integration in accounts.DefaultIfEmpty()
            select new { session.Id, user.Email, Game = game.Title, game.CapsuleUrl,
                PlatformKey = playedPlatform.Key, Platform = playedPlatform.Name, AccountId = integration == null ? null : integration.AccountId,
                Source = session.CaptureMethod, session.StartedAt, session.EndedAt,
                LastPolledAt = session.CaptureMethod == CaptureMethod.Polling && session.IntegrationId != null
                    ? db.PollResults.Where(poll => poll.IntegrationId == session.IntegrationId)
                        .Select(poll => (DateTimeOffset?)poll.PolledAt).Max() : null };
        var liveRows = rows.Where(row => row.EndedAt == null);
        if (!string.IsNullOrWhiteSpace(query))
        {
            var like = $"%{query.Trim()}%";
            liveRows = liveRows.Where(row => EF.Functions.ILike(row.Email, like)
                || EF.Functions.ILike(row.Game, like) || (row.AccountId != null && EF.Functions.ILike(row.AccountId, like)));
        }
        if (!string.IsNullOrWhiteSpace(platform)) liveRows = liveRows.Where(row => row.PlatformKey == platform);
        var liveSessions = await liveRows.OrderBy(row => row.StartedAt).ThenBy(row => row.Id).Select(row => new DashboardSession(row.Id, row.Email, row.Game, row.CapsuleUrl, row.PlatformKey, row.Platform, row.AccountId, row.Source, row.StartedAt, row.EndedAt, row.LastPolledAt)).ToListAsync(ct);
        var ended = await rows.Where(row => row.EndedAt != null)
            .OrderByDescending(row => row.EndedAt).ThenByDescending(row => row.Id).Take(10).Select(row => new DashboardSession(row.Id, row.Email, row.Game, row.CapsuleUrl, row.PlatformKey, row.Platform, row.AccountId, row.Source, row.StartedAt, row.EndedAt, row.LastPolledAt)).ToListAsync(ct);
        var generations = await (from generation in failures
            join user in db.Users.AsNoTracking() on generation.UserId equals user.Id
            orderby generation.CreatedAt descending, generation.Id descending
            select new DashboardGeneration(generation.Id, user.Email, generation.Model, generation.Status, generation.CreatedAt))
            .Take(10).ToListAsync(ct);
        var platforms = await db.Platforms.AsNoTracking().OrderBy(row => row.Name)
            .Select(row => new DashboardPlatform(row.Key, row.Name)).ToListAsync(ct);
        return new(now, counts, platforms, liveSessions, ended, generations);
    }
}
