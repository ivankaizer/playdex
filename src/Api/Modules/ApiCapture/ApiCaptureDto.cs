using System.Text.Json;

namespace Api.Modules.ApiCapture;

public sealed record ApiSessionRequest(
    int? DurationInSeconds,
    string GameTitle,
    JsonElement? GameId,
    string? GameIdProvider = null,
    string? Platform = null
);

public sealed record ApiSessionResponseDto(long SessionId);

public sealed record ApiSessionStartResponseDto(long SessionId);

public sealed record ApiSessionEndResponseDto(int EndedSessionCount);

public sealed record ApiSessionPingResponseDto(
    long SessionId,
    bool Started,
    int EndedSessionCount
);

public sealed record ApiCapturePlatformDto(string Key, string Name);
