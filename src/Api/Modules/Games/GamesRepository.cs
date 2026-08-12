using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Common;
using Api.Infrastructure.Persistence;

namespace Api.Modules.Games;

public sealed class GamesRepository(PlaydexDbContext db)
{
    public async Task<List<GameSearchResultDto>> SearchGames(string query, int limit, CancellationToken ct)
    {
        var pattern = $"%{query}%";
        var rows = await db.Games
            .AsNoTracking()
            .Where(game => EF.Functions.ILike(game.Title, pattern))
            .OrderByDescending(game => game.UpdatedAt)
            .Take(limit)
            .Select(game => new { game.Id, game.Title, Platform = "unknown", game.CapsuleUrl })
            .ToListAsync(ct);
        return rows.Select(r => new GameSearchResultDto(GameId.Encode(r.Id), r.Title, r.Platform, r.CapsuleUrl)).ToList();
    }

    public async Task<GameResponseDto?> GetGameById(long gameId, CancellationToken ct)
    {
        var row = await db.Games
            .AsNoTracking()
            .Where(game => game.Id == gameId)
            .Select(game => new { game.Id, game.Title, Platform = "unknown", game.CapsuleUrl })
            .FirstOrDefaultAsync(ct);
        return row is null ? null : new GameResponseDto(GameId.Encode(row.Id), row.Title, row.Platform, row.CapsuleUrl);
    }

    public async Task<List<(long GameId, long ConnectedGameId)>> ListConnectionEdges(long userId, CancellationToken ct)
    {
        var edges = await db.GameConnections
            .AsNoTracking()
            .Where(gc => gc.UserId == userId)
            .Select(gc => new { gc.GameId, gc.ConnectedGameId })
            .ToListAsync(ct);
        return edges.Select(e => (e.GameId, e.ConnectedGameId)).ToList();
    }

    public async Task<List<GameConnectionDto>> ListConnections(long userId, CancellationToken ct)
    {
        var rows = await (
            from connection in db.GameConnections.AsNoTracking()
            join game in db.Games.AsNoTracking() on connection.GameId equals game.Id
            join connectedGame in db.Games.AsNoTracking() on connection.ConnectedGameId equals connectedGame.Id
            where connection.UserId == userId
            orderby connection.GameId, connection.ConnectedGameId
            select new
            {
                connection.GameId,
                game.Title,
                Platform = "unknown",
                game.CapsuleUrl,
                connection.ConnectedGameId,
                ConnectedTitle = connectedGame.Title,
                ConnectedPlatform = "unknown",
                ConnectedCapsuleUrl = connectedGame.CapsuleUrl,
            }).ToListAsync(ct);
        return rows.Select(r => new GameConnectionDto(
            GameId.Encode(r.GameId), r.Title, r.Platform, r.CapsuleUrl,
            GameId.Encode(r.ConnectedGameId), r.ConnectedTitle, r.ConnectedPlatform, r.ConnectedCapsuleUrl)).ToList();
    }

    public async Task CreateConnection(long userId, long gameId, long connectedGameId, CancellationToken ct)
    {
        var (g, c) = GameConnections.Normalize(gameId, connectedGameId);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO game_connections (user_id, game_id, connected_game_id) VALUES ({userId}, {g}, {c}) ON CONFLICT DO NOTHING
            """, ct);
    }

    public async Task DeleteConnection(long userId, long gameId, long connectedGameId, CancellationToken ct)
    {
        var (g, c) = GameConnections.Normalize(gameId, connectedGameId);
        await db.GameConnections
            .Where(gc => gc.UserId == userId && gc.GameId == g && gc.ConnectedGameId == c)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<bool> GetIgnoreStatus(long userId, long gameId, CancellationToken ct)
    {
        return await db.IgnoredGames.AnyAsync(ig => ig.UserId == userId && ig.GameId == gameId, ct);
    }

    public async Task SetIgnored(long userId, long gameId, bool ignored, CancellationToken ct)
    {
        if (ignored)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO ignored_games (user_id, game_id) VALUES ({userId}, {gameId}) ON CONFLICT DO NOTHING
                """, ct);
        }
        else
        {
            await db.IgnoredGames
                .Where(ig => ig.UserId == userId && ig.GameId == gameId)
                .ExecuteDeleteAsync(ct);
        }
    }

    public async Task<int?> GetRating(long userId, long gameId, CancellationToken ct)
    {
        return await db.GameRatings
            .AsNoTracking()
            .Where(gr => gr.UserId == userId && gr.GameId == gameId)
            .Select(gr => (int?)gr.Rating)
            .FirstOrDefaultAsync(ct);
    }

    public async Task SetRating(long userId, long gameId, int rating, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO game_ratings (user_id, game_id, rating, created_at, updated_at)
            VALUES ({userId}, {gameId}, {rating}, {now}, {now})
            ON CONFLICT (user_id, game_id) DO UPDATE SET rating = {rating}, updated_at = {now}
            """, ct);
    }

    public async Task DeleteRating(long userId, long gameId, CancellationToken ct)
    {
        await db.GameRatings
            .Where(gr => gr.UserId == userId && gr.GameId == gameId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<Game?> GetGameEntity(long gameId, CancellationToken ct) =>
        await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);

}
