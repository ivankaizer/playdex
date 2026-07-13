namespace Playdex.Web.Models;

public sealed record GameSearchResult(string GameId, string Title, string Platform, string? CapsuleUrl);
public sealed record GameSearchResponse(string Query, IReadOnlyList<GameSearchResult> Results);
public sealed record GameResponse(string Id, string Title, string Platform, string? CapsuleUrl);
public sealed record GameConnection(
    string GameId,
    string Title,
    string Platform,
    string? CapsuleUrl,
    string ConnectedGameId,
    string ConnectedTitle,
    string ConnectedPlatform,
    string? ConnectedCapsuleUrl);
public sealed record GameConnectionsResponse(IReadOnlyList<GameConnection> Connections);
public sealed record GameConnectionChangeRequest(string GameId, string ConnectedGameId);
public sealed record GameConnectionChangeResponse(string GameId, string ConnectedGameId);
public sealed record GameIgnoreRequest(string GameId);
public sealed record GameIgnoreResponse(string GameId, bool Ignored);
public sealed record GameRatingRequest(string GameId, int? Rating);
public sealed record GameRatingResponse(string GameId, int? Rating);
