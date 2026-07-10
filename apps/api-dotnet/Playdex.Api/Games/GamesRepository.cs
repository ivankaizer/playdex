using Microsoft.EntityFrameworkCore;
using Playdex.Api.Common;
using Playdex.Api.Data;
using Dapper;
using Npgsql;

namespace Playdex.Api.Games;

public interface IGamesRepository
{
    Task<List<GameSearchResultDto>> SearchGames(string query, int limit, CancellationToken ct);
    Task<GameResponseDto?> GetGameById(long gameId, CancellationToken ct);
    Task<List<(long GameId, long ConnectedGameId)>> ListConnectionEdges(long userId, CancellationToken ct);
    Task<List<GameConnectionDto>> ListConnections(long userId, CancellationToken ct);
    Task CreateConnection(long userId, long gameId, long connectedGameId, CancellationToken ct);
    Task DeleteConnection(long userId, long gameId, long connectedGameId, CancellationToken ct);
    Task<bool> GetIgnoreStatus(long userId, long gameId, CancellationToken ct);
    Task SetIgnored(long userId, long gameId, bool ignored, CancellationToken ct);
    Task<int?> GetRating(long userId, long gameId, CancellationToken ct);
    Task SetRating(long userId, long gameId, int rating, CancellationToken ct);
    Task DeleteRating(long userId, long gameId, CancellationToken ct);
    Task<Game?> GetGameEntity(long gameId, CancellationToken ct);
}

public sealed class GamesRepository(NpgsqlDataSource dataSource, PlaydexDbContext db) : IGamesRepository
{
    public async Task<List<GameSearchResultDto>> SearchGames(string query, int limit, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<GameSearchRow>(
            """SELECT g.id, g.title, p.name AS platform, g.capsule_url FROM games g JOIN platforms p ON p.id = g.platform_id WHERE g.title ILIKE @Query ORDER BY g.title LIMIT @Limit""",
            new { Query = $"%{query}%", Limit = limit });
        return rows.Select(r => new GameSearchResultDto(GameId.Encode(r.Id), r.Title, r.Platform, r.CapsuleUrl)).ToList();
    }

    public async Task<GameResponseDto?> GetGameById(long gameId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<GameSearchRow>(
            """SELECT g.id, g.title, p.name AS platform, g.capsule_url FROM games g JOIN platforms p ON p.id = g.platform_id WHERE g.id = @Id""",
            new { Id = gameId });
        return row is null ? null : new GameResponseDto(GameId.Encode(row.Id), row.Title, row.Platform, row.CapsuleUrl);
    }

    public async Task<List<(long GameId, long ConnectedGameId)>> ListConnectionEdges(long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<(long, long)>(
            "SELECT game_id, connected_game_id FROM game_connections WHERE user_id = @UserId",
            new { UserId = userId });
        return rows.ToList();
    }

    public async Task<List<GameConnectionDto>> ListConnections(long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ConnectionRow>("""
            SELECT gc.game_id, g1.title AS title, p1.name AS platform, g1.capsule_url,
                   gc.connected_game_id, g2.title AS connected_title, p2.name AS connected_platform, g2.capsule_url AS connected_capsule_url
            FROM game_connections gc
            JOIN games g1 ON g1.id = gc.game_id
            JOIN platforms p1 ON p1.id = g1.platform_id
            JOIN games g2 ON g2.id = gc.connected_game_id
            JOIN platforms p2 ON p2.id = g2.platform_id
            WHERE gc.user_id = @UserId
            """, new { UserId = userId });
        return rows.Select(r => new GameConnectionDto(
            GameId.Encode(r.GameId), r.Title, r.Platform, r.CapsuleUrl,
            GameId.Encode(r.ConnectedGameId), r.ConnectedTitle, r.ConnectedPlatform, r.ConnectedCapsuleUrl)).ToList();
    }

    public async Task CreateConnection(long userId, long gameId, long connectedGameId, CancellationToken ct)
    {
        var (g, c) = GameConnections.Normalize(gameId, connectedGameId);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "INSERT INTO game_connections (user_id, game_id, connected_game_id) VALUES (@UserId, @GameId, @ConnectedId) ON CONFLICT DO NOTHING",
            new { UserId = userId, GameId = g, ConnectedId = c });
    }

    public async Task DeleteConnection(long userId, long gameId, long connectedGameId, CancellationToken ct)
    {
        var (g, c) = GameConnections.Normalize(gameId, connectedGameId);
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM game_connections WHERE user_id = @UserId AND game_id = @GameId AND connected_game_id = @ConnectedId",
            new { UserId = userId, GameId = g, ConnectedId = c });
    }

    public async Task<bool> GetIgnoreStatus(long userId, long gameId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS(SELECT 1 FROM ignored_games WHERE user_id = @UserId AND game_id = @GameId)",
            new { UserId = userId, GameId = gameId });
    }

    public async Task SetIgnored(long userId, long gameId, bool ignored, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        if (ignored)
        {
            await conn.ExecuteAsync(
                "INSERT INTO ignored_games (user_id, game_id) VALUES (@UserId, @GameId) ON CONFLICT DO NOTHING",
                new { UserId = userId, GameId = gameId });
        }
        else
        {
            await conn.ExecuteAsync(
                "DELETE FROM ignored_games WHERE user_id = @UserId AND game_id = @GameId",
                new { UserId = userId, GameId = gameId });
        }
    }

    public async Task<int?> GetRating(long userId, long gameId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<int?>(
            "SELECT rating FROM game_ratings WHERE user_id = @UserId AND game_id = @GameId",
            new { UserId = userId, GameId = gameId });
    }

    public async Task SetRating(long userId, long gameId, int rating, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync("""
            INSERT INTO game_ratings (user_id, game_id, rating, created_at, updated_at)
            VALUES (@UserId, @GameId, @Rating, @Now, @Now)
            ON CONFLICT (user_id, game_id) DO UPDATE SET rating = @Rating, updated_at = @Now
            """, new { UserId = userId, GameId = gameId, Rating = rating, Now = now });
    }

    public async Task DeleteRating(long userId, long gameId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "DELETE FROM game_ratings WHERE user_id = @UserId AND game_id = @GameId",
            new { UserId = userId, GameId = gameId });
    }

    public async Task<Game?> GetGameEntity(long gameId, CancellationToken ct) =>
        await db.Games.AsNoTracking().FirstOrDefaultAsync(g => g.Id == gameId, ct);

    private sealed class GameSearchRow
    {
        public long Id { get; set; }
        public required string Title { get; set; }
        public required string Platform { get; set; }
        public string? CapsuleUrl { get; set; }
    }

    private sealed class ConnectionRow
    {
        public long GameId { get; set; }
        public required string Title { get; set; }
        public required string Platform { get; set; }
        public string? CapsuleUrl { get; set; }
        public long ConnectedGameId { get; set; }
        public required string ConnectedTitle { get; set; }
        public required string ConnectedPlatform { get; set; }
        public string? ConnectedCapsuleUrl { get; set; }
    }
}
