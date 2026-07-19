namespace Playdex.Api.Modules.Games;

public sealed record GameSearchResultDto(string GameId, string Title, string Platform, string? CapsuleUrl);
public sealed record GameSearchResponseDto(string Query, IReadOnlyList<GameSearchResultDto> Results);
public sealed record GameResponseDto(string Id, string Title, string Platform, string? CapsuleUrl);

public sealed record GameConnectionDto(
    string GameId, string Title, string Platform, string? CapsuleUrl,
    string ConnectedGameId, string ConnectedTitle, string ConnectedPlatform, string? ConnectedCapsuleUrl);
public sealed record GameConnectionListResponseDto(IReadOnlyList<GameConnectionDto> Connections);
public sealed record GameConnectionChangeRequest(string GameId, string ConnectedGameId);
public sealed record GameConnectionChangeResponseDto(string GameId, string ConnectedGameId);

public sealed record GameIgnoreRequest(string GameId);
public sealed record GameIgnoreResponseDto(string GameId, bool Ignored);

public sealed record GameRatingRequest(string GameId, int? Rating);
public sealed record GameRatingResponseDto(string GameId, int? Rating);
