using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Api.Modules.Games;
using Api.Modules.Posters;
using Api.Modules.Sessions;

namespace Api.Modules.ApiCapture;

public sealed class ApiCaptureService(
    ApiCaptureRepository repository,
    PlaydexDbContext db,
    IGameResolver gameResolver,
    SessionLifecycle sessions,
    PosterUrls posterUrls)
{
    public async Task<IReadOnlyList<ApiCapturePlatformDto>?> ListPlatforms(
        string apiKey,
        CancellationToken ct)
    {
        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        return credential is null ? null : await repository.ListPcPlatforms(ct);
    }

    public Task<ApiSessionResponseDto?> RecordSession(
        string apiKey, int durationInSeconds, DateTimeOffset? startedAt, string gameTitle, string gameId, string? gameProvider,
        string platform, CancellationToken ct) =>
        Capture(apiKey, async credential =>
        {
            var (resolvedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);
            var (start, end) = RecordedRange(DateTimeOffset.UtcNow, durationInSeconds, startedAt);
            IReadOnlyList<(DateTimeOffset StartedAt, DateTimeOffset EndedAt)> pieces = [(start, end)];
            if (startedAt is not null)
            {
                // A session with its real start can be one the server partly has already: play a client sends after an
                // outage longer than the ping timeout (the server closed its session 3 minutes after the last ping it got),
                // or a send repeated because its answer was lost. Only the time the server doesn't have yet is recorded.
                // Capture holds the integration's session lock, so two sends of the same session can't both miss each other.
                var recorded = await repository.ListSessionRanges(credential.Id, resolvedGameId, platformId, start, end, ct);
                pieces = Uncovered(start, end, recorded);
            }

            long? sessionId = null;
            var recordedSeconds = 0;
            foreach (var (from, to) in pieces)
            {
                var id = await sessions.RecordCapture(
                    credential.Id, credential.UserId, resolvedGameId, platformId, from, to, ct);
                sessionId ??= id;
                recordedSeconds += (int)(to - from).TotalSeconds;
            }

            return new ApiSessionResponseDto(sessionId, recordedSeconds);
        }, ct);

    /// <summary>
    /// The parts of <paramref name="start"/>..<paramref name="end"/> that none of <paramref name="recorded"/> (sessions
    /// the server has, an ongoing one with no end) overlap, in order; parts under a second are left out.
    /// </summary>
    public static IReadOnlyList<(DateTimeOffset StartedAt, DateTimeOffset EndedAt)> Uncovered(
        DateTimeOffset start, DateTimeOffset end, IEnumerable<(DateTimeOffset StartedAt, DateTimeOffset? EndedAt)> recorded)
    {
        var pieces = new List<(DateTimeOffset, DateTimeOffset)>();
        void Add(DateTimeOffset from, DateTimeOffset to)
        {
            if (to - from >= TimeSpan.FromSeconds(1))
            {
                pieces.Add((from, to));
            }
        }

        var cursor = start;
        foreach (var (from, to) in recorded.OrderBy(session => session.StartedAt))
        {
            if (from >= end)
            {
                break;
            }
            var until = to ?? DateTimeOffset.MaxValue;
            if (until <= cursor)
            {
                continue;
            }
            if (from > cursor)
            {
                Add(cursor, from);
            }
            cursor = until;
            if (cursor >= end)
            {
                return pieces;
            }
        }
        Add(cursor, end);
        return pieces;
    }

    /// <summary>
    /// When a recorded session ran: from <paramref name="startedAt"/> for <paramref name="durationInSeconds"/> (a session
    /// a client kept offline and sends later), or ending <paramref name="now"/> without a start. A start that would end
    /// the session after now (a client clock ahead of the server's) ends it now instead, so no session is in the future.
    /// </summary>
    public static (DateTimeOffset StartedAt, DateTimeOffset EndedAt) RecordedRange(
        DateTimeOffset now, int durationInSeconds, DateTimeOffset? startedAt)
    {
        var duration = TimeSpan.FromSeconds(durationInSeconds);
        return startedAt is { } start && start + duration <= now
            ? (start.ToUniversalTime(), (start + duration).ToUniversalTime())
            : (now - duration, now);
    }


    public Task<ApiSessionStartResponseDto?> StartSession(
        string apiKey, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct) =>
        Capture(apiKey, async credential =>
        {
            var (resolvedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);
            var outcome = await sessions.StartCapture(
                credential.Id, credential.UserId, resolvedGameId, platformId, ct);
            return new ApiSessionStartResponseDto(outcome.SessionId);
        }, ct);

    public Task<ApiSessionEndResponseDto?> EndSessions(
        string apiKey, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct) =>
        Capture(apiKey, async credential =>
        {
            var (resolvedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);
            return new ApiSessionEndResponseDto(
                await sessions.EndCapture(credential.Id, resolvedGameId, platformId, ct));
        }, ct);

    public Task<ApiSessionPingResponseDto?> PingSession(
        string apiKey, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct) =>
        Capture(apiKey, async credential =>
        {
            var (resolvedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);
            var outcome = await sessions.PingCapture(
                credential.Id, credential.UserId, resolvedGameId, platformId, ct);
            return new ApiSessionPingResponseDto(outcome.SessionId, outcome.Started, outcome.EndedSessionCount);
        }, ct);

    /// <summary>Runs one capture call under its integration's session lock and announces its session changes.</summary>
    private async Task<T?> Capture<T>(
        string apiKey,
        Func<ApiIntegrationRow, Task<T>> work,
        CancellationToken ct) where T : class
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        await sessions.LockIntegration(credential.Id, ct);
        var result = await work(credential);
        await transaction.CommitAsync(ct);
        await sessions.PublishPending(ct);
        return result;
    }

    private async Task<(long GameId, long PlatformId)> ResolveGame(
        string gameTitle,
        string gameId,
        string? gameProvider,
        string platform,
        CancellationToken ct)
    {
        var platformKey = platform.Trim().ToLowerInvariant();
        var platformId = await db.Platforms.Where(row => row.Key == platformKey)
            .Select(row => (long?)row.Id).SingleOrDefaultAsync(ct)
            ?? throw Api.Infrastructure.Http.AppProblemException.Validation("Unknown platform.");
        var provider = gameProvider is null ? null : GameExternalIdentityTypes.FromKey(gameProvider);
        var resolution = await gameResolver.Resolve(
            new GameResolutionRequest(
                provider,
                gameId,
                gameTitle,
                GenerateCapsuleUrl(gameProvider ?? "", gameId)),
            ct);
        return (resolution.GameId, platformId);
    }

    private string? GenerateCapsuleUrl(string gameProvider, string gameId) =>
        string.Equals(gameProvider, "steam", StringComparison.OrdinalIgnoreCase)
            ? posterUrls.SteamCapsule(gameId)
            : null;
}
