using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Api.Infrastructure.Common;
using Shared.Persistence;
using Api.Modules.Games;

namespace Api.Modules.ApiCapture;

public interface IApiSessionTimeoutCleaner
{
    Task<int> EndTimedOutSessions(CancellationToken ct);
}

public sealed partial class ApiCaptureService(
    ApiCaptureRepository repository,
    PlaydexDbContext db,
    IGameResolver gameResolver,
    EventBus eventBus,
    ILogger<ApiCaptureService> logger) : IApiSessionTimeoutCleaner
{
    private const int PingTimeoutMinutes = 3;

    [LoggerMessage(Level = LogLevel.Error, Message = "API capture event handler failed for {EventType}")]
    private partial void LogApiEventHandlerFailed(Exception exception, string eventType);

    public async Task<IReadOnlyList<ApiCapturePlatformDto>?> ListPlatforms(
        string apiKey,
        CancellationToken ct)
    {
        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        return credential is null ? null : await repository.ListPcPlatforms(ct);
    }

    public async Task<ApiSessionResponseDto?> RecordSession(
        string apiKey, int durationInSeconds, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var (insertedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);

        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddSeconds(-durationInSeconds);
        var sessionId = await repository.InsertCompletePlaySession(credential, insertedGameId, platformId, startedAt, now, ct);

        await tx.CommitAsync(ct);
        return new ApiSessionResponseDto(sessionId);
    }

    public async Task<ApiSessionStartResponseDto?> StartSession(
        string apiKey, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var (insertedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);

        var now = DateTimeOffset.UtcNow;
        await repository.EndActiveSessionsForCredential(credential.Id, now, ct);

        var sessionId = await repository.InsertActivePlaySession(credential, insertedGameId, platformId, now, ct);

        await tx.CommitAsync(ct);
        return new ApiSessionStartResponseDto(sessionId);
    }

    public async Task<ApiSessionEndResponseDto?> EndSessions(
        string apiKey, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var (resolvedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);
        var endedSessions = await db.PlaySessions
            .Where(row =>
                row.IntegrationId == credential.Id &&
                row.GameId == resolvedGameId &&
                row.PlatformId == platformId &&
                row.EndedAt == null)
            .ToListAsync(ct);
        foreach (var session in endedSessions)
        {
            session.EndedAt = now;
            session.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        foreach (var session in endedSessions)
        {
            await SafePublish(new SessionEnded(
                session.Id, credential.Id, session.GameId, session.StartedAt, now), ct);
        }

        return new ApiSessionEndResponseDto(endedSessions.Count);
    }

    public async Task<ApiSessionPingResponseDto?> PingSession(
        string apiKey, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var (insertedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);

        var now = DateTimeOffset.UtcNow;
        var timeoutBefore = now.AddMinutes(-PingTimeoutMinutes);

        var timedOut = await db.PlaySessions
            .Where(row =>
                row.IntegrationId == credential.Id &&
                row.EndedAt == null &&
                row.UpdatedAt < timeoutBefore)
            .ToListAsync(ct);
        foreach (var session in timedOut)
        {
            session.EndedAt = session.UpdatedAt.AddMinutes(PingTimeoutMinutes);
            session.UpdatedAt = now;
        }
        await db.SaveChangesAsync(ct);

        var active = await db.PlaySessions
            .Where(row =>
                row.IntegrationId == credential.Id &&
                row.GameId == insertedGameId &&
                row.PlatformId == platformId &&
                row.EndedAt == null)
            .OrderByDescending(row => row.StartedAt)
            .FirstOrDefaultAsync(ct);

        if (active is not null && active.GameId == insertedGameId)
        {
            active.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            foreach (var session in timedOut)
            {
                await SafePublish(new SessionEnded(
                    session.Id,
                    session.IntegrationId!.Value,
                    session.GameId,
                    session.StartedAt,
                    session.EndedAt!.Value), ct);
            }
            return new ApiSessionPingResponseDto(active.Id, false, timedOut.Count);
        }

        await repository.EndActiveSessionsForCredential(credential.Id, now, ct);
        var sessionId = await repository.InsertActivePlaySession(credential, insertedGameId, platformId, now, ct);
        await tx.CommitAsync(ct);
        foreach (var session in timedOut)
        {
            await SafePublish(new SessionEnded(
                session.Id,
                session.IntegrationId!.Value,
                session.GameId,
                session.StartedAt,
                session.EndedAt!.Value), ct);
        }
        await SafePublish(new SessionStarted(sessionId, credential.Id, insertedGameId, now), ct);
        return new ApiSessionPingResponseDto(sessionId, true, timedOut.Count);
    }

    public async Task<int> EndTimedOutSessions(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var timeoutBefore = now.AddMinutes(-PingTimeoutMinutes);
        var timedOut = await repository.EndTimedOutActiveSessions(
            timeoutBefore, now, PingTimeoutMinutes, ct);
        await tx.CommitAsync(ct);

        foreach (var session in timedOut)
        {
            await SafePublish(new SessionEnded(
                session.Id,
                session.IntegrationId,
                session.GameId,
                session.StartedAt,
                session.EndedAt), ct);
        }

        return timedOut.Count;
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

    private static string? GenerateCapsuleUrl(string gameProvider, string gameId)
    {
        if (string.Equals(gameProvider, "steam", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(gameId))
        {
            return $"https://shared.steamstatic.com/store_item_assets/steam/apps/{gameId}/library_600x900_2x.jpg";
        }

        return null;
    }

    private async Task SafePublish<T>(T @event, CancellationToken ct)
    {
        try
        {
            await eventBus.Publish(@event, ct);
        }
        catch (Exception ex)
        {
            LogApiEventHandlerFailed(ex, typeof(T).Name);
        }
    }
}
