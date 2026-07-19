using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Modules.Polling;

public sealed class PollingRepository(PlaydexDbContext db)
{
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
        var now = DateTimeOffset.UtcNow;
        var pollResult = new PollResult
        {
            PlatformCredentialId = credentialId,
            GameId = gameId,
            PolledAt = polledAt,
            RawPayload = rawPayload != null ? System.Text.Json.JsonDocument.Parse(rawPayload) : null,
            CreatedAt = now,
        };
        db.PollResults.Add(pollResult);
        await db.SaveChangesAsync(ct);
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
