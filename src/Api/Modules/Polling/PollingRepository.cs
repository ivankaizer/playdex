using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Api.Modules.Games;

namespace Api.Modules.Polling;

public sealed class PollingRepository(PlaydexDbContext db, IGameResolver gameResolver)
{
    public async Task<SuccessfulPollOutcome> ApplySuccessfulPoll(
        long credentialId,
        CurrentlyPlayingResult? detectedGame,
        DateTimeOffset polledAt,
        string? rawPayload,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({credentialId})", ct);

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

        db.PollResults.Add(new PollResult
        {
            IntegrationId = credentialId,
            GameExternalIdentityId = identityId,
            PolledAt = polledAt,
            RawPayload = rawPayload,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var active = await db.PlaySessions
            .Where(row => row.IntegrationId == credentialId && row.EndedAt == null)
            .OrderByDescending(row => row.StartedAt)
            .FirstOrDefaultAsync(ct);
        var transitions = new List<SessionTransition>();
        PlaySession? startedSession = null;
        var sessionGameId = ignored ? null : gameId;

        if (active is not null && active.GameId != sessionGameId)
        {
            active.EndedAt = polledAt;
            active.UpdatedAt = DateTimeOffset.UtcNow;
            transitions.Add(SessionTransition.Ended(active, polledAt, notify: !ignored));
            active = null;
        }

        if (active is not null)
        {
            active.UpdatedAt = polledAt;
        }
        else if (sessionGameId is not null)
        {
            var session = new PlaySession
            {
                GameId = sessionGameId.Value,
                UserId = integration.UserId,
                PlatformId = platformId,
                CaptureMethod = CaptureMethod.Polling,
                IntegrationId = credentialId,
                StartedAt = polledAt,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = polledAt,
            };
            db.PlaySessions.Add(session);
            startedSession = session;
        }

        await db.SaveChangesAsync(ct);
        if (startedSession is not null)
        {
            transitions.Add(SessionTransition.Started(startedSession, notify: true));
        }
        await transaction.CommitAsync(ct);
        return new SuccessfulPollOutcome(gameId, ignored, needsCapsule, transitions);
    }

    public async Task<List<ActiveCredential>> LoadActiveCredentials(CancellationToken ct)
    {
        var rows = await db.Integrations
            .AsNoTracking()
            .Where(pc => pc.Active && pc.Type != IntegrationType.Api)
            .Select(pc => new ActiveCredentialRow
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
            })
            .ToListAsync(ct);

        return rows.Select(r => new ActiveCredential(
            new IntegrationCredentials
            {
                Id = r.Id,
                UserId = r.UserId,
                ApiKey = r.ApiKey,
                AccountId = r.AccountId,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                Active = r.Active,
                PlatformId = r.PlatformId,
            },
            GetProviderKey(r.Type)
        )).ToList();
    }

    public async Task<bool> CheckIfGameIgnored(long userId, long gameId, CancellationToken ct)
    {
        return await db.IgnoredGames.AnyAsync(ig => ig.UserId == userId && ig.GameId == gameId, ct);
    }

    public async Task RecordPollResult(
        long credentialId, long? gameId,
        DateTimeOffset polledAt, string? rawPayload,
        CancellationToken ct)
    {
        db.PollResults.Add(
            new PollResult
            {
                IntegrationId = credentialId,
                PolledAt = polledAt,
                RawPayload = rawPayload,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
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
public sealed record ActiveCredential(
    IntegrationCredentials Credential,
    string PlatformKey
);

public enum SessionTransitionKind
{
    Started,
    Ended,
}

public sealed record SessionTransition(
    SessionTransitionKind Kind,
    long SessionId,
    long CredentialId,
    long GameId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    bool Notify)
{
    public static SessionTransition Started(PlaySession session, bool notify) => new(
        SessionTransitionKind.Started,
        session.Id,
        session.IntegrationId!.Value,
        session.GameId,
        session.StartedAt,
        null,
        notify);

    public static SessionTransition Ended(
        PlaySession session,
        DateTimeOffset endedAt,
        bool notify) => new(
            SessionTransitionKind.Ended,
            session.Id,
            session.IntegrationId!.Value,
            session.GameId,
            session.StartedAt,
            endedAt,
            notify);
}

public sealed record SuccessfulPollOutcome(
    long? GameId,
    bool Ignored,
    bool NeedsCapsule,
    IReadOnlyList<SessionTransition> Transitions);
