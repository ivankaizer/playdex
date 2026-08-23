using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Manual;

public sealed class ManualRepository(PlaydexDbContext db)
{
    public async Task<T> ExecuteInTransaction<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await action();
        await transaction.CommitAsync(ct);
        return result;
    }

    public Task<int> EndActiveSessions(long userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"update play_sessions set ended_at=now(),updated_at=now() where user_id={userId} and capture_method='manual' and ended_at is null", ct);

    public async Task<long> CreatePlaySession(long userId, long gameId, long platformId, DateTimeOffset startedAt, DateTimeOffset? endedAt, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new PlaySession { UserId = userId, GameId = gameId, PlatformId = platformId, CaptureMethod = CaptureMethod.Manual, StartedAt = startedAt, EndedAt = endedAt, CreatedAt = now, UpdatedAt = now };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session.Id;
    }

    public async Task<List<GameSearchRow>> SearchLocalGames(string query, int limit, CancellationToken ct) =>
        await db.Games.AsNoTracking().Where(g => EF.Functions.ILike(g.Title, $"%{query}%"))
            .OrderByDescending(g => g.UpdatedAt).Take(limit)
            .Select(g => new GameSearchRow { Id = g.Id, Title = g.Title, Platform = "unknown", CapsuleUrl = g.CapsuleUrl }).ToListAsync(ct);

    public async Task<long> CreateGame(string title, string? capsuleUrl, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var game = new Game { Title = title, CapsuleUrl = capsuleUrl, CreatedAt = now, UpdatedAt = now };
        db.Games.Add(game); await db.SaveChangesAsync(ct); return game.Id;
    }

    public Task<long?> GetPlatformIdByKey(string key, CancellationToken ct) =>
        db.Platforms.Where(p => p.Key == key).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct);
    public Task<string?> GetPlatformKeyByIgdbId(int id, CancellationToken ct) =>
        db.Platforms.Where(p => p.IgdbPlatformId == id).Select(p => p.Key).FirstOrDefaultAsync(ct);
    public Task<Game?> GetGameEntity(long id, CancellationToken ct) => db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);
}

public sealed class GameSearchRow { public long Id { get; set; } public string Title { get; set; } = ""; public string Platform { get; set; } = ""; public string? CapsuleUrl { get; set; } }
