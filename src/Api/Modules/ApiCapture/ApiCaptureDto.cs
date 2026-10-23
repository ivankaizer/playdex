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

/// <summary>A recorded session (POST /capture/sessions).</summary>
/// <param name="SessionId">
/// The (first) session recorded; null when the server already had all of that time (a session sent with its startedAt
/// is only recorded where it doesn't overlap the API key's sessions of that game, so a repeated send counts once).
/// </param>
/// <param name="RecordedSeconds">How much of it was new; it may come as more than one session around what was there.</param>
public sealed record ApiSessionResponseDto(long? SessionId, int RecordedSeconds);

public sealed record ApiSessionStartResponseDto(long SessionId);

public sealed record ApiSessionEndResponseDto(int EndedSessionCount);

public sealed record ApiSessionPingResponseDto(
    long SessionId,
    bool Started,
    int EndedSessionCount
);

public sealed record ApiCapturePlatformDto(string Key, string Name);
