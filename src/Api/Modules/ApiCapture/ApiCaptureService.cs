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
        string apiKey, int durationInSeconds, string gameTitle, string gameId, string? gameProvider, string platform,
        CancellationToken ct) =>
        Capture(apiKey, async credential =>
        {
            var (resolvedGameId, platformId) = await ResolveGame(gameTitle, gameId, gameProvider, platform, ct);
            var now = DateTimeOffset.UtcNow;
            var sessionId = await sessions.RecordCapture(
                credential.Id, credential.UserId, resolvedGameId, platformId, now.AddSeconds(-durationInSeconds), now, ct);
            return new ApiSessionResponseDto(sessionId);
        }, ct);

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
