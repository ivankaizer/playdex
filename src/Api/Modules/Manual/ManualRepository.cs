using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using System.Text.RegularExpressions;

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

    internal Task AcquireUserStartLock(long userId, CancellationToken ct)
    {
        var key = $"manual-session-user:{userId}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            ct);
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

    public async Task<List<GameSearchRow>> SearchRecentlyPlayedGames(
        long userId,
        string query,
        int limit,
        CancellationToken ct)
    {
        IQueryable<Game> games = db.Games.AsNoTracking();
        foreach (var term in SignificantTitleTerms(query))
        {
            var pattern = $"%{term}%";
            games = games.Where(game => EF.Functions.ILike(game.Title, pattern));
        }

        return await games
            .Where(game => db.PlaySessions.Any(session =>
                session.UserId == userId && session.GameId == game.Id))
            .OrderByDescending(game => db.PlaySessions
                .Where(session => session.UserId == userId && session.GameId == game.Id)
                .Max(session => (DateTimeOffset?)session.StartedAt))
            .Take(limit)
            .Select(game => new GameSearchRow
            {
                Id = game.Id,
                Title = game.Title,
                Platform = (from session in db.PlaySessions
                            join platform in db.Platforms on session.PlatformId equals platform.Id
                            where session.UserId == userId && session.GameId == game.Id
                            orderby session.StartedAt descending, session.Id descending
                            select platform.Key).First(),
                CapsuleUrl = game.CapsuleUrl,
            })
            .ToListAsync(ct);
    }

    private static string[] SignificantTitleTerms(string query)
    {
        var terms = Regex.Split(query.Trim().ToLowerInvariant(), "[^\\p{L}\\p{Nd}]+")
            .Where(term => term.Length >= 3 && term is not ("the" or "and"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return terms.Length > 0 ? terms : [query.Trim()];
    }

    public Task<long?> GetPlatformIdByKey(string key, CancellationToken ct) =>
        db.Platforms.Where(p => p.Key == key).Select(p => (long?)p.Id).FirstOrDefaultAsync(ct);
    public Task<Game?> GetGameEntity(long id, CancellationToken ct) => db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, ct);

    public Task<List<Platform>> ListPlatforms(CancellationToken ct) =>
        db.Platforms.AsNoTracking().ToListAsync(ct);

    internal Task<ManualActiveSessionRow?> LoadActiveSession(long userId, CancellationToken ct) =>
        (from session in db.PlaySessions.AsNoTracking()
         join game in db.Games.AsNoTracking() on session.GameId equals game.Id
         join platform in db.Platforms.AsNoTracking() on session.PlatformId equals platform.Id
         where session.UserId == userId &&
               session.CaptureMethod == CaptureMethod.Manual &&
               session.EndedAt == null
         orderby session.StartedAt descending
         select new ManualActiveSessionRow(
             session.Id,
             game.Id,
             game.Title,
             platform.Key,
             platform.Name,
             session.StartedAt,
             game.CapsuleUrl))
        .FirstOrDefaultAsync(ct);
}

public sealed class GameSearchRow { public long Id { get; set; } public string Title { get; set; } = ""; public string Platform { get; set; } = ""; public string? CapsuleUrl { get; set; } }
