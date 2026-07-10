using Dapper;
using Npgsql;

namespace Playdex.Api.Providers;

public interface IProvidersRepository
{
    Task<List<ProviderStatusDto>> ListProviders(long userId, CancellationToken ct);
    Task<ProviderStatusDto?> UpdateProviderStatus(long userId, long providerId, string status, CancellationToken ct);
    Task<string> GetOrCreatePlayniteKey(long userId, string apiKey, CancellationToken ct);
}

public sealed class ProvidersRepository(NpgsqlDataSource dataSource) : IProvidersRepository
{
    public async Task<List<ProviderStatusDto>> ListProviders(long userId, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<ProviderRow>("""
            SELECT pc.id, p.key AS platform, pc.account_id,
                   CASE WHEN pc.active THEN 'active' ELSE 'paused' END AS status,
                   pc.api_key
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            WHERE pc.user_id = @UserId
            """, new { UserId = userId });

        return rows.Select(r => new ProviderStatusDto(r.Id, r.Platform, r.AccountId, r.Status, r.ApiKey)).ToList();
    }

    public async Task<ProviderStatusDto?> UpdateProviderStatus(long userId, long providerId, string status, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<ProviderRow>("""
            WITH updated AS (
                UPDATE platform_credentials
                SET active = (@Status = 'active'), updated_at = NOW()
                WHERE id = @Id AND user_id = @UserId
                RETURNING id, platform_id, account_id, active, api_key
            )
            SELECT updated.id, p.key AS platform, updated.account_id,
                   CASE WHEN updated.active THEN 'active' ELSE 'paused' END AS status,
                   updated.api_key
            FROM updated
            JOIN platforms p ON p.id = updated.platform_id
            """, new { UserId = userId, Id = providerId, Status = status });

        if (row is null) return null;
        return new ProviderStatusDto(row.Id, row.Platform, row.AccountId, row.Status, row.ApiKey);
    }

    public async Task<string> GetOrCreatePlayniteKey(long userId, string apiKey, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);

        var existing = await conn.QueryFirstOrDefaultAsync<string>("""
            SELECT pc.api_key FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            WHERE pc.user_id = @UserId AND p.key = 'playnite'
            """, new { UserId = userId });

        if (existing is not null) return existing;

        await conn.ExecuteAsync("""
            INSERT INTO platform_credentials (user_id, platform_id, api_key, active, created_at, updated_at)
            SELECT @UserId, id, @ApiKey, true, NOW(), NOW() FROM platforms WHERE key = 'playnite'
            """, new { UserId = userId, ApiKey = apiKey });

        return apiKey;
    }

    private sealed class ProviderRow
    {
        public long Id { get; set; }
        public required string Platform { get; set; }
        public string? AccountId { get; set; }
        public required string Status { get; set; }
        public string? ApiKey { get; set; }
    }
}
