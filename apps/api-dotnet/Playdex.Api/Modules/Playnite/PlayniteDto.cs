namespace Playdex.Api.Modules.Playnite;

public sealed record PlayniteSessionRequest(
    int? DurationInSeconds,
    string GameTitle,
    string GameId,
    string GameProvider
);

public sealed record PlayniteSessionResponseDto(long SessionId);

public sealed record PlayniteSessionStartResponseDto(long SessionId);

public sealed record PlayniteSessionEndResponseDto(int EndedSessionCount);

public sealed record PlayniteSessionPingResponseDto(
    long SessionId,
    bool Started,
    int EndedSessionCount
);
