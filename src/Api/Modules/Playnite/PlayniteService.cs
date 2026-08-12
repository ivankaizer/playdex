using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Api.Infrastructure.Common;
using Api.Infrastructure.Persistence;
using Api.Modules.Games;

namespace Api.Modules.Playnite;

public sealed partial class PlayniteService(
    PlayniteRepository repository,
    PlaydexDbContext db,
    IGameResolver gameResolver,
    EventBus eventBus,
    ILogger<PlayniteService> logger)
{
    private const int PingTimeoutMinutes = 3;

    [LoggerMessage(Level = LogLevel.Error, Message = "Playnite event handler failed for {EventType}")]
    private partial void LogPlayniteEventHandlerFailed(Exception exception, string eventType);

    public async Task<PlayniteSessionResponseDto?> RecordSession(
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
        return new PlayniteSessionResponseDto(sessionId);
    }

    public async Task<PlayniteSessionStartResponseDto?> StartSession(
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
        return new PlayniteSessionStartResponseDto(sessionId);
    }

    public async Task<PlayniteSessionEndResponseDto?> EndSessions(
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

        return new PlayniteSessionEndResponseDto(endedSessions.Count);
    }

    public async Task<PlayniteSessionPingResponseDto?> PingSession(
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
                    session.PlatformCredentialId,
                    session.GameId,
                    session.StartedAt,
                    session.EndedAt!.Value), ct);
            }
            return new PlayniteSessionPingResponseDto(active.Id, false, timedOut.Count);
        }

        await repository.EndActiveSessionsForCredential(credential.Id, now, ct);
        var sessionId = await repository.InsertActivePlaySession(credential, insertedGameId, platformId, now, ct);
        await tx.CommitAsync(ct);
        foreach (var session in timedOut)
        {
            await SafePublish(new SessionEnded(
                session.Id,
                session.PlatformCredentialId,
                session.GameId,
                session.StartedAt,
                session.EndedAt!.Value), ct);
        }
        await SafePublish(new SessionStarted(sessionId, credential.Id, insertedGameId, now), ct);
        return new PlayniteSessionPingResponseDto(sessionId, true, timedOut.Count);
    }

    public async Task<int> EndTimedOutSessions(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var timeoutBefore = now.AddMinutes(-PingTimeoutMinutes);
        return await repository.EndTimedOutActiveSessions(timeoutBefore, now, PingTimeoutMinutes, ct);
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
                platformKey,
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
            LogPlayniteEventHandlerFailed(ex, typeof(T).Name);
        }
    }
}
