using Dapper;
using Npgsql;
using Playdex.Api.Data;

namespace Playdex.Api.Polling;

public interface IPollingRepository
{
    Task<List<ActiveCredential>> LoadActiveCredentials(CancellationToken ct);
    Task<bool> CheckIfGameIgnored(long userId, long gameId, CancellationToken ct);
    Task RecordPollResult(long credentialId, long? gameId, DateTimeOffset polledAt, string? rawPayload, CancellationToken ct);
}

public sealed class PollingRepository(NpgsqlDataSource dataSource) : IPollingRepository
{
    public async Task<List<ActiveCredential>> LoadActiveCredentials(CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ActiveCredentialRow>(
            """
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
            """);

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
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                FROM ignored_games
                WHERE user_id = @UserId AND game_id = @GameId
            )
            """,
            new { UserId = userId, GameId = gameId });
    }

    public async Task RecordPollResult(
        long credentialId, long? gameId,
        DateTimeOffset polledAt, string? rawPayload,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        await conn.ExecuteAsync(
            """
            INSERT INTO poll_results (platform_credential_id, game_id, polled_at, raw_payload, created_at)
            VALUES (@CredentialId, @GameId, @PolledAt, @RawPayload::jsonb, @Now)
            """,
            new
            {
                CredentialId = credentialId,
                GameId = gameId,
                PolledAt = polledAt,
                RawPayload = rawPayload ?? "null",
                Now = now,
            });
    }

    // ── Internal row types for Dapper mapping ───────────────────

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
