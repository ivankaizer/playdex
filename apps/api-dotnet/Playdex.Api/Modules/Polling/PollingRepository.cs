using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Polling;

public sealed class PollingRepository(PlaydexDbContext db)
{
    public async Task<IReadOnlyList<SessionTransition>> EndTimedOutPlayniteSessions(
        DateTimeOffset now,
        TimeSpan timeout,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT pg_advisory_xact_lock(-783521)", ct);
        var timeoutBefore = now - timeout;
        var playniteCredentialIds = db.PlatformCredentials
            .Join(
                db.Platforms.Where(row => row.Key == "playnite"),
                credential => credential.PlatformId,
                platform => platform.Id,
                (credential, _) => credential.Id);
        var expired = await db.PlaySessions
            .Where(row =>
                playniteCredentialIds.Contains(row.PlatformCredentialId) &&
                row.EndedAt == null &&
                row.UpdatedAt < timeoutBefore)
            .ToListAsync(ct);

        var transitions = new List<SessionTransition>(expired.Count);
        foreach (var session in expired)
        {
            var endedAt = session.UpdatedAt + timeout;
            session.EndedAt = endedAt;
            session.UpdatedAt = now;
            transitions.Add(SessionTransition.Ended(session, endedAt, notify: true));
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return transitions;
    }

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

        long? gameId = null;
        var ignored = false;
        if (detectedGame is not null)
        {
            gameId = await UpsertGame(detectedGame, ct);
            ignored = await db.IgnoredGames
                .Join(
                    db.PlatformCredentials.Where(row => row.Id == credentialId),
                    ignoredGame => ignoredGame.UserId,
                    credential => credential.UserId,
                    (ignoredGame, _) => ignoredGame)
                .AnyAsync(row => row.GameId == gameId, ct);
        }

        db.PollResults.Add(new PollResult
        {
            PlatformCredentialId = credentialId,
            GameId = gameId,
            PolledAt = polledAt,
            RawPayload = rawPayload,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        var active = await db.PlaySessions
            .Where(row => row.PlatformCredentialId == credentialId && row.EndedAt == null)
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
                PlatformCredentialId = credentialId,
                GameId = sessionGameId.Value,
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
        return new SuccessfulPollOutcome(gameId, ignored, transitions);
    }

    public async Task<List<ActiveCredential>> LoadActiveCredentials(CancellationToken ct)
    {
        var rows = await db.Database.SqlQuery<ActiveCredentialRow>($"""
                                                                    SELECT
                                                                        pc.id                        AS Id,
                                                                        pc.user_id                   AS UserId,
                                                                        pc.api_key                   AS ApiKey,
                                                                        pc.account_id                AS AccountId,
                                                                        pc.created_at                AS CreatedAt,
                                                                        pc.updated_at                AS UpdatedAt,
                                                                        pc.active                    AS Active,
                                                                        pc.platform_id               AS PlatformId,
                                                                        p.key                        AS PlatformKey
                                                                    FROM platform_credentials pc
                                                                    JOIN platforms p ON p.id = pc.platform_id
                                                                    WHERE pc.active
                                                                      AND p.key != 'playnite'
                                                                    """).ToListAsync(ct);

        return rows.Select(r => new ActiveCredential(
            new PlatformCredential
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
            r.PlatformKey
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
                PlatformCredentialId = credentialId,
                GameId = gameId,
                PolledAt = polledAt,
                RawPayload = rawPayload,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync(ct);
    }

    private async Task<long> UpsertGame(CurrentlyPlayingResult game, CancellationToken ct)
    {
        var platformId = await db.Platforms
            .Where(row => row.Key == game.Platform)
            .Select(row => (long?)row.Id)
            .FirstOrDefaultAsync(ct)
            ?? await db.Platforms
                .Where(row => row.Key == "unknown")
                .Select(row => row.Id)
                .SingleAsync(ct);
        var title = game.Title.Trim();
        var externalId = string.IsNullOrWhiteSpace(game.ExternalId)
            ? null
            : game.ExternalId.Trim();
        var canonicalKey = $"{game.Platform}:{externalId ?? NormalizeTitle(title)}";
        var now = DateTimeOffset.UtcNow;

        var ids = await db.Database.SqlQuery<long>($"""
            INSERT INTO games
                (platform_id, title, external_id, canonical_key, capsule_url, created_at, updated_at)
            VALUES
                ({platformId}, {title}, {externalId}, {canonicalKey}, {game.CapsuleUrl}, {now}, {now})
            ON CONFLICT (canonical_key) DO UPDATE SET
                title = CASE WHEN games.title_locked THEN games.title ELSE EXCLUDED.title END,
                platform_id = EXCLUDED.platform_id,
                external_id = EXCLUDED.external_id,
                capsule_url = CASE
                    WHEN games.capsule_url_locked THEN games.capsule_url
                    ELSE COALESCE(EXCLUDED.capsule_url, games.capsule_url)
                END,
                updated_at = EXCLUDED.updated_at
            RETURNING id
            """).ToListAsync(ct);
        return ids.Single();
    }

    private static string NormalizeTitle(string title)
    {
        var characters = title.ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : ' ')
            .ToArray();
        return string.Join(' ',
            new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
        public string PlatformKey { get; set; } = string.Empty;
    }
}

/// <summary>
/// A platform credential enriched with its platform key for routing
/// to the correct <see cref="IProviderClient"/>.
/// </summary>
public sealed record ActiveCredential(
    PlatformCredential Credential,
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
        session.PlatformCredentialId,
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
            session.PlatformCredentialId,
            session.GameId,
            session.StartedAt,
            endedAt,
            notify);
}

public sealed record SuccessfulPollOutcome(
    long? GameId,
    bool Ignored,
    IReadOnlyList<SessionTransition> Transitions);
