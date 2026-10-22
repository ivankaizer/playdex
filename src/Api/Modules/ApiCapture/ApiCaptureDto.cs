using System.Text.Json;

namespace Api.Modules.ApiCapture;

/// <param name="StartedAt">
/// POST /capture/sessions only, optional: when a session sent after it ended (a client's offline queue) started.
/// Without it the session ends now.
/// </param>
public sealed record ApiSessionRequest(
    int? DurationInSeconds,
    string GameTitle,
    JsonElement? GameId,
    string? GameIdProvider = null,
    string? Platform = null,
    DateTimeOffset? StartedAt = null
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
