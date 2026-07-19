using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Playnite;

public sealed class PlayniteService(PlayniteRepository repository, PlaydexDbContext db)
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
        string apiKey,
        CancellationToken ct)
    {
        var credential = await repository.LoadCredentialByApiKey(apiKey, ct);
        if (credential is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var endedCount = await repository.EndActiveSessionsForCredential(credential.Id, now, ct);

        return new PlayniteSessionEndResponseDto(endedCount);
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

        var endedTimedOut = await repository.EndTimedOutActiveSessionsForCredential(
            credential.Id, timeoutBefore, now, PingTimeoutMinutes, ct);

        var endedMismatched = await repository.EndActiveSessionsForCredentialExceptGame(
            credential.Id, insertedGameId, now, ct);

        var endedSessionCount = endedTimedOut + endedMismatched;

        var active = await repository.FetchActiveSessionForCredential(credential.Id, ct);

        if (active is not null && active.GameId == insertedGameId)
        {
            await repository.TouchActiveSession(active.Id, now, ct);
            await tx.CommitAsync(ct);
            return new PlayniteSessionPingResponseDto(active.Id, false, endedSessionCount);
        }

        var sessionId = await repository.InsertActivePlaySession(credential.Id, insertedGameId, now, ct);
        await tx.CommitAsync(ct);
        return new PlayniteSessionPingResponseDto(sessionId, true, endedSessionCount);
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
}
