using Playdex.Api.Common;
using Playdex.Api.Http;

namespace Playdex.Api.Games;

public sealed class GamesService(IGamesRepository repository)
{
    public async Task<GameSearchResponseDto> Search(string query, int limit, CancellationToken ct)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return new GameSearchResponseDto(query, []);
        }

        var results = await repository.SearchGames(trimmed, Math.Clamp(limit, 1, 50), ct);
        return new GameSearchResponseDto(query, results);
    }

    public async Task<GameResponseDto> GetGame(string gameId, CancellationToken ct)
    {
        var id = GameId.Parse(gameId);
        var game = await repository.GetGameById(id, ct);
        return game ?? throw AppProblem.NotFound("Game not found.");
    }

    public async Task<GameConnectionListResponseDto> ListConnections(long userId, CancellationToken ct)
    {
        var connections = await repository.ListConnections(userId, ct);
        return new GameConnectionListResponseDto(connections);
    }

    public async Task<GameConnectionChangeResponseDto> Connect(
        long userId, GameConnectionChangeRequest request, CancellationToken ct)
    {
        var gameId = GameId.Parse(request.GameId);
        var connectedId = GameId.Parse(request.ConnectedGameId);

        if (gameId == connectedId)
        {
            throw AppProblem.Validation("Cannot connect a game to itself.");
        }

        await repository.CreateConnection(userId, gameId, connectedId, ct);
        return new GameConnectionChangeResponseDto(request.GameId, request.ConnectedGameId);
    }

    public async Task<GameConnectionChangeResponseDto> Disconnect(
        long userId, GameConnectionChangeRequest request, CancellationToken ct)
    {
        var gameId = GameId.Parse(request.GameId);
        var connectedId = GameId.Parse(request.ConnectedGameId);
        await repository.DeleteConnection(userId, gameId, connectedId, ct);
        return new GameConnectionChangeResponseDto(request.GameId, request.ConnectedGameId);
    }

    public async Task<GameIgnoreResponseDto> GetIgnore(long userId, string? gameId, CancellationToken ct)
    {
        if (gameId is null) throw AppProblem.Validation("gameId is required.");
        var id = GameId.Parse(gameId);
        var ignored = await repository.GetIgnoreStatus(userId, id, ct);
        return new GameIgnoreResponseDto(gameId, ignored);
    }

    public async Task<GameIgnoreResponseDto> SetIgnore(long userId, GameIgnoreRequest request, bool ignored, CancellationToken ct)
    {
        var gameId = GameId.Parse(request.GameId);
        await repository.SetIgnored(userId, gameId, ignored, ct);
        return new GameIgnoreResponseDto(request.GameId, ignored);
    }

    public async Task<GameRatingResponseDto> GetRating(long userId, string? gameId, CancellationToken ct)
    {
        if (gameId is null) throw AppProblem.Validation("gameId is required.");
        var id = GameId.Parse(gameId);
        var rating = await repository.GetRating(userId, id, ct);
        return new GameRatingResponseDto(gameId, rating);
    }

    public async Task<GameRatingResponseDto> SetRating(long userId, GameRatingRequest request, CancellationToken ct)
    {
        var gameId = GameId.Parse(request.GameId);
        var rating = request.Rating;
        if (rating is null || rating < 1 || rating > 5)
        {
            throw AppProblem.Validation("Rating must be between 1 and 5.");
        }
        await repository.SetRating(userId, gameId, rating.Value, ct);
        return new GameRatingResponseDto(request.GameId, rating);
    }

    public async Task<GameRatingResponseDto> DeleteRating(long userId, GameRatingRequest request, CancellationToken ct)
    {
        var gameId = GameId.Parse(request.GameId);
        await repository.DeleteRating(userId, gameId, ct);
        return new GameRatingResponseDto(request.GameId, null);
    }
}
