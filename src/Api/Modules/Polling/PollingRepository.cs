using System.Security.Cryptography;
using Api.Infrastructure.Credentials;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Api.Modules.Games;
using Api.Modules.Sessions;

namespace Api.Modules.Polling;

public sealed class PollingRepository(
    PlaydexDbContext db, IGameResolver gameResolver, SessionLifecycle sessions, CredentialProtector protector)
{
    public async Task<SuccessfulPollOutcome> ApplySuccessfulPoll(
        long credentialId,
        CurrentlyPlayingResult? detectedGame,
        DateTimeOffset polledAt,
        string? rawPayload,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await sessions.LockIntegration(credentialId, ct);

        var integration = await db.Integrations.SingleAsync(row => row.Id == credentialId, ct);
        var platformId = GetEffectiveSessionPlatformId(integration);
        long? gameId = null;
        long? identityId = null;
        var ignored = false;
        var needsCapsule = false;
        if (detectedGame is not null)
        {
            var resolution = await gameResolver.Resolve(
                new GameResolutionRequest(
                    GameExternalIdentityTypes.FromKey(detectedGame.Platform),
                    detectedGame.ExternalId,
                    detectedGame.Title,
                    detectedGame.CapsuleUrl,
                    TitleIsAuthoritative: true),
                ct);
            gameId = resolution.GameId;
            identityId = resolution.GameExternalIdentityId;
            needsCapsule = resolution.NeedsCapsule;
            ignored = await db.IgnoredGames
                .Join(
                    db.Integrations.Where(row => row.Id == credentialId),
                    ignoredGame => ignoredGame.UserId,
                    credential => credential.UserId,
                    (ignoredGame, _) => ignoredGame)
                .AnyAsync(row => row.GameId == gameId, ct);
        }

        var previousPoll = await db.PollResults
            .AsNoTracking()
            .Where(row => row.IntegrationId == credentialId)
            .OrderByDescending(row => row.PolledAt)
            .Select(row => new { row.GameExternalIdentityId })
            .FirstOrDefaultAsync(ct);

        var sessionsChanged = await sessions.ApplyPoll(
            credentialId, integration.UserId, ignored ? null : gameId, platformId, polledAt, ct);

        // A row per poll records when the integration was last polled; the raw response (large, and full of
        // profile data) is kept only when what the user plays changed.
        var changed = previousPoll is null
            || previousPoll.GameExternalIdentityId != identityId
            || sessionsChanged;
        db.PollResults.Add(new PollResult
        {
            IntegrationId = credentialId,
            GameExternalIdentityId = identityId,
            PolledAt = polledAt,
            RawPayload = changed ? rawPayload : null,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new SuccessfulPollOutcome(gameId, ignored, needsCapsule);
    }

    public async Task<List<ActiveCredential>> LoadActiveCredentials(CancellationToken ct)
    {
        var rows = await (
            from pc in db.Integrations.AsNoTracking()
            where pc.Active && pc.Type != IntegrationType.Api
            join backoff in db.IntegrationPollBackoffs.AsNoTracking()
                on pc.Id equals backoff.IntegrationId into backoffs
            from backoff in backoffs.DefaultIfEmpty()
            select new ActiveCredentialRow
            {
                Id = pc.Id,
                UserId = pc.UserId,
                ApiKey = pc.ApiKey,
                AccountId = pc.AccountId,
                CreatedAt = pc.CreatedAt,
                UpdatedAt = pc.UpdatedAt,
                Active = pc.Active,
                PlatformId = pc.PlatformId!.Value,
                Type = pc.Type,
                NextPollAt = backoff == null ? null : backoff.NextPollAt,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ActiveCredential(
            new IntegrationCredentials
            {
                Id = r.Id,
                UserId = r.UserId,
                ApiKey = Decrypt(r.Id, r.ApiKey),
                AccountId = r.AccountId,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                Active = r.Active,
                PlatformId = r.PlatformId,
            },
            GetProviderKey(r.Type),
            r.NextPollAt
        )).ToList();
    }

    /// <summary>Every polled integration, active or not, with its failures and when it last polled successfully.</summary>
    public async Task<List<IntegrationHealth>> LoadIntegrationHealth(CancellationToken ct)
    {
        var rows = await (
            from integration in db.Integrations.AsNoTracking()
            where integration.Type != IntegrationType.Api
            join backoff in db.IntegrationPollBackoffs.AsNoTracking()
                on integration.Id equals backoff.IntegrationId into backoffs
            from backoff in backoffs.DefaultIfEmpty()
            select new
            {
                integration.Id,
                integration.UserId,
                integration.Type,
                integration.Active,
                integration.PausedAt,
                Failures = backoff == null ? 0 : backoff.Failures,
                FailingSince = backoff == null ? null : backoff.FailingSince,
                LastPolledAt = db.PollResults
                    .Where(poll => poll.IntegrationId == integration.Id)
                    .Max(poll => (DateTimeOffset?)poll.PolledAt),
            })
            .ToListAsync(ct);

        return rows.Select(row => new IntegrationHealth(
            row.Id,
            row.UserId,
            GetProviderKey(row.Type),
            row.Active,
            row.PausedAt,
            row.Failures,
            row.FailingSince,
            row.LastPolledAt)).ToList();
    }

    private string? Decrypt(long integrationId, string? apiKey)
    {
        try
        {
            return protector.Unprotect(apiKey);
        }
        catch (CryptographicException exception)
        {
            throw new CryptographicException(
                $"The credential of integration {integrationId} can't be decrypted. Was CREDENTIALS_KEY changed?", exception);
        }
    }

    /// <summary>
    /// Stops polling an integration whose credential the provider keeps rejecting, recording why so that the user
    /// sees it. Only an active integration is paused, so of several pollers only one does it. Its backoff stays until
    /// the user resumes it, so a poll that was already running counts on and does not announce a new outage.
    /// </summary>
    /// <returns>True when this call paused it.</returns>
    public async Task<bool> PauseIntegration(long credentialId, string reason, DateTimeOffset pausedAt, CancellationToken ct)
    {
        var paused = await db.Integrations
            .Where(integration => integration.Id == credentialId && integration.Active)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(integration => integration.Active, false)
                    .SetProperty(integration => integration.PausedReason, reason)
                    .SetProperty(integration => integration.PausedAt, pausedAt)
                    .SetProperty(integration => integration.UpdatedAt, pausedAt),
                ct);
        return paused > 0;
    }

    public async Task UpdateGameCapsule(long gameId, string capsuleUrl, CancellationToken ct)
    {
        await db.Games
            .Where(game =>
                game.Id == gameId &&
                game.CapsuleUrl == null &&
                !game.CapsuleUrlLocked)
            .ExecuteUpdateAsync(
                updates => updates
                    .SetProperty(game => game.CapsuleUrl, capsuleUrl)
                    .SetProperty(game => game.UpdatedAt, DateTimeOffset.UtcNow),
                ct);
    }

    private sealed class ActiveCredentialRow
    {
        public long Id { get; set; }
        public long UserId { get; set; }
        public string? ApiKey { get; set; }
        public string? AccountId { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool Active { get; set; }
        public long PlatformId { get; set; }
        public IntegrationType Type { get; set; }
        public DateTimeOffset? NextPollAt { get; set; }
    }

    internal static long GetEffectiveSessionPlatformId(Integration integration) =>
        integration.SessionPlatformId ?? integration.PlatformId
        ?? throw new InvalidOperationException("Polling Integration requires a Platform.");

    internal static string GetProviderKey(IntegrationType type) => type switch
    {
        IntegrationType.Steam => "steam",
        IntegrationType.Psn => "psn",
        IntegrationType.Xbox => "xbox",
        _ => throw new InvalidOperationException($"Integration type '{type}' does not support polling."),
    };
}

/// <summary>
/// A platform credential enriched with its platform key for routing
/// to the correct <see cref="IProviderClient"/>.
/// </summary>
/// <param name="NextPollAt">
/// Set when the credential has backoff state (it is failing, or its provider asked to be left alone): it is not
/// polled before then.
/// </param>
public sealed record ActiveCredential(
    IntegrationCredentials Credential,
    string PlatformKey,
    DateTimeOffset? NextPollAt = null
);

public sealed record SuccessfulPollOutcome(
    long? GameId,
    bool Ignored,
    bool NeedsCapsule);
