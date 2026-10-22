using Api.Infrastructure.Common;
using Api.Infrastructure.Http;

namespace Api.Modules.Games;

public sealed class GamesService(GamesRepository repository) : IGames
{
    public string NormalizeTitle(string title) => GamesDomain.NormalizeTitle(title);

    public HashSet<long> ResolveConnectedIds(long gameId, List<(long GameId, long ConnectedGameId)> edges)
        => GameConnections.ResolveConnectedIds(gameId, edges);

    public Dictionary<long, double> MergeGameTotals(Dictionary<long, double> gameTotals, List<(long GameId, long ConnectedGameId)> edges)
        => GameConnections.MergeGameTotals(gameTotals, edges);

    public async Task<GameSearchResponseDto> Search(string query, int limit, CancellationToken ct)
    {
        var trimmed = SearchText.Bounded(query);
        if (trimmed.Length == 0)
        {
            throw AppProblemException.Validation("query is required.");
        }

        var results = await repository.SearchGames(trimmed, Math.Clamp(limit, 1, 50), ct);
        return new GameSearchResponseDto(query, results);
    }

    public async Task<GameResponseDto> GetGame(string gameId, CancellationToken ct)
    {
        var id = GameId.Parse(gameId);
        var game = await repository.GetGameById(id, ct);
        return game ?? throw AppProblemException.NotFound("Game not found.");
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
            throw AppProblemException.Validation("gameId and connectedGameId must be different.");
        }

        var first = await repository.GetGameEntity(gameId, ct);
        var second = await repository.GetGameEntity(connectedId, ct);
        if (first is null || second is null)
            throw AppProblemException.NotFound("Both games must exist to connect.");

        await repository.CreateConnection(userId, gameId, connectedId, ct);
        var normalized = GameConnections.Normalize(gameId, connectedId);
        return new GameConnectionChangeResponseDto(
            GameId.Encode(normalized.First),
            GameId.Encode(normalized.Second));
    }

    public async Task<GameConnectionChangeResponseDto> Disconnect(
        long userId, GameConnectionChangeRequest request, CancellationToken ct)
    {
        var gameId = GameId.Parse(request.GameId);
        var connectedId = GameId.Parse(request.ConnectedGameId);
        if (gameId == connectedId)
            throw AppProblemException.Validation("gameId and connectedGameId must be different.");
        await repository.DeleteConnection(userId, gameId, connectedId, ct);
        var normalized = GameConnections.Normalize(gameId, connectedId);
        return new GameConnectionChangeResponseDto(
            GameId.Encode(normalized.First),
            GameId.Encode(normalized.Second));
    }

    public async Task<GameIgnoreResponseDto> GetIgnore(long userId, string? gameId, CancellationToken ct)
    {
        if (gameId is null) throw AppProblemException.Validation("gameId is required.");
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
        if (gameId is null) throw AppProblemException.Validation("gameId is required.");
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
            throw AppProblemException.Validation("Rating must be between 1 and 5.");
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
