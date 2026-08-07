using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Common;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Playnite;

public sealed class PlayniteService(
    PlayniteRepository repository,
    PlaydexDbContext db,
    EventBus eventBus,
    ILogger<PlayniteService> logger)
{
    private const int PingTimeoutMinutes = 3;

    public async Task<PlayniteSessionResponseDto?> RecordSession(
        string apiKey, int durationInSeconds, string gameTitle, string gameId, string gameProvider,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var canonicalKey = BuildCanonicalKey(gameProvider, gameId);
        var capsuleUrl = GenerateCapsuleUrl(gameProvider, gameId);
        var insertedGameId = await repository.UpsertGame(gameTitle, gameId, gameProvider, canonicalKey, capsuleUrl, ct);

        var now = DateTimeOffset.UtcNow;
        var startedAt = now.AddSeconds(-durationInSeconds);
        var sessionId = await repository.InsertCompletePlaySession(credential.Id, insertedGameId, startedAt, now, ct);

        await tx.CommitAsync(ct);
        return new PlayniteSessionResponseDto(sessionId);
    }

    public async Task<PlayniteSessionStartResponseDto?> StartSession(
        string apiKey, string gameTitle, string gameId, string gameProvider,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var canonicalKey = BuildCanonicalKey(gameProvider, gameId);
        var capsuleUrl = GenerateCapsuleUrl(gameProvider, gameId);
        var insertedGameId = await repository.UpsertGame(gameTitle, gameId, gameProvider, canonicalKey, capsuleUrl, ct);

        var now = DateTimeOffset.UtcNow;
        await repository.EndActiveSessionsForCredential(credential.Id, now, ct);

        var sessionId = await repository.InsertActivePlaySession(credential.Id, insertedGameId, now, ct);

        await tx.CommitAsync(ct);
        return new PlayniteSessionStartResponseDto(sessionId);
    }

    public async Task<PlayniteSessionEndResponseDto?> EndSessions(
        string apiKey, string gameTitle, string gameId, string gameProvider,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var canonicalKey = BuildCanonicalKey(gameProvider, gameId);
        var capsuleUrl = GenerateCapsuleUrl(gameProvider, gameId);
        var resolvedGameId = await repository.UpsertGame(
            gameTitle, gameId, gameProvider, canonicalKey, capsuleUrl, ct);
        var endedSessions = await db.PlaySessions
            .Where(row =>
                row.PlatformCredentialId == credential.Id &&
                row.GameId == resolvedGameId &&
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
        string apiKey, string gameTitle, string gameId, string gameProvider,
        CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var canonicalKey = BuildCanonicalKey(gameProvider, gameId);
        var capsuleUrl = GenerateCapsuleUrl(gameProvider, gameId);
        var insertedGameId = await repository.UpsertGame(gameTitle, gameId, gameProvider, canonicalKey, capsuleUrl, ct);

        var now = DateTimeOffset.UtcNow;
        var timeoutBefore = now.AddMinutes(-PingTimeoutMinutes);

        var timedOut = await db.PlaySessions
            .Where(row =>
                row.PlatformCredentialId == credential.Id &&
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
                row.PlatformCredentialId == credential.Id &&
                row.GameId == insertedGameId &&
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

        var sessionId = await repository.InsertActivePlaySession(credential.Id, insertedGameId, now, ct);
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

    private static string BuildCanonicalKey(string gameProvider, string gameId)
    {
        return $"{gameProvider}:{gameId}";
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
            logger.LogError(ex, "Playnite event handler failed for {EventType}", typeof(T).Name);
        }
    }
}
