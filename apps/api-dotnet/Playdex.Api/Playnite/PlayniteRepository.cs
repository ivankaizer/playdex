using Dapper;
using Npgsql;

namespace Playdex.Api.Playnite;

public interface IPlayniteRepository
{
    Task<PlayniteCredentialRow?> LoadCredentialByApiKey(NpgsqlConnection conn, string apiKey, CancellationToken ct);
    Task<long> UpsertGame(NpgsqlConnection conn, NpgsqlTransaction tx, string title, string? externalId, string platformKey, string canonicalKey, string? capsuleUrl, CancellationToken ct);
    Task<long> InsertCompletePlaySession(NpgsqlConnection conn, NpgsqlTransaction tx, long credentialId, long gameId, DateTimeOffset startedAt, DateTimeOffset endedAt, CancellationToken ct);
    Task<long> InsertActivePlaySession(NpgsqlConnection conn, NpgsqlTransaction tx, long credentialId, long gameId, DateTimeOffset startedAt, CancellationToken ct);
    Task<int> EndActiveSessionsForCredential(NpgsqlConnection conn, NpgsqlTransaction? tx, long credentialId, DateTimeOffset endedAt, CancellationToken ct);
    Task<int> EndActiveSessionsForCredentialExceptGame(NpgsqlConnection conn, NpgsqlTransaction tx, long credentialId, long gameId, DateTimeOffset endedAt, CancellationToken ct);
    Task<int> EndTimedOutActiveSessionsForCredential(NpgsqlConnection conn, NpgsqlTransaction tx, long credentialId, DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes, CancellationToken ct);
    Task<ActiveSessionRow?> FetchActiveSessionForCredential(NpgsqlConnection conn, NpgsqlTransaction tx, long credentialId, CancellationToken ct);
    Task<bool> TouchActiveSession(NpgsqlConnection conn, NpgsqlTransaction tx, long sessionId, DateTimeOffset touchedAt, CancellationToken ct);
    Task<int> EndTimedOutActiveSessions(DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes, CancellationToken ct);
}

public sealed class PlayniteRepository(NpgsqlDataSource dataSource) : IPlayniteRepository
{
    public async Task<PlayniteCredentialRow?> LoadCredentialByApiKey(NpgsqlConnection conn, string apiKey, CancellationToken ct)
    {
        return await conn.QueryFirstOrDefaultAsync<PlayniteCredentialRow>(
            """
            SELECT pc.id
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            WHERE pc.api_key = @ApiKey
              AND p.key = 'playnite'
              AND pc.active = true
            """,
            new { ApiKey = apiKey });
    }

    public async Task<long> UpsertGame(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        string title, string? externalId, string platformKey, string canonicalKey, string? capsuleUrl,
        CancellationToken ct)
    {
        var platformId = await conn.QueryFirstOrDefaultAsync<long?>(
            "SELECT id FROM platforms WHERE key = @Key",
            new { Key = platformKey },
            transaction: tx);

        if (platformId is null)
        {
            platformId = await conn.QueryFirstOrDefaultAsync<long>(
                "SELECT id FROM platforms WHERE key = 'unknown'",
                transaction: tx);
        }

        var now = DateTimeOffset.UtcNow;
        return await conn.QueryFirstOrDefaultAsync<long>(
            """
            INSERT INTO games (platform_id, title, external_id, canonical_key, capsule_url, created_at, updated_at)
            VALUES (@PlatformId, @Title, @ExternalId, @CanonicalKey, @CapsuleUrl, @Now, @Now)
            ON CONFLICT (canonical_key) DO UPDATE SET
                title = CASE WHEN games.title_locked THEN games.title ELSE EXCLUDED.title END,
                platform_id = EXCLUDED.platform_id,
                external_id = EXCLUDED.external_id,
                capsule_url = CASE WHEN games.capsule_url_locked THEN games.capsule_url ELSE COALESCE(EXCLUDED.capsule_url, games.capsule_url) END,
                updated_at = NOW()
            RETURNING id
            """,
            new
            {
                PlatformId = platformId!.Value,
                Title = title,
                ExternalId = externalId,
                CanonicalKey = canonicalKey,
                CapsuleUrl = capsuleUrl,
                Now = now,
            },
            transaction: tx);
    }

    public async Task<long> InsertCompletePlaySession(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        long credentialId, long gameId, DateTimeOffset startedAt, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await conn.QueryFirstOrDefaultAsync<long>(
            """
            INSERT INTO play_sessions (platform_credential_id, game_id, started_at, ended_at, created_at, updated_at)
            VALUES (@CredentialId, @GameId, @StartedAt, @EndedAt, @Now, @Now)
            RETURNING id
            """,
            new { CredentialId = credentialId, GameId = gameId, StartedAt = startedAt, EndedAt = endedAt, Now = now },
            transaction: tx);
    }

    public async Task<long> InsertActivePlaySession(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        long credentialId, long gameId, DateTimeOffset startedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await conn.QueryFirstOrDefaultAsync<long>(
            """
            INSERT INTO play_sessions (platform_credential_id, game_id, started_at, created_at, updated_at)
            VALUES (@CredentialId, @GameId, @StartedAt, @Now, @Now)
            RETURNING id
            """,
            new { CredentialId = credentialId, GameId = gameId, StartedAt = startedAt, Now = now },
            transaction: tx);
    }

    public async Task<int> EndActiveSessionsForCredential(
        NpgsqlConnection conn, NpgsqlTransaction? tx,
        long credentialId, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await conn.ExecuteAsync(
            """
            UPDATE play_sessions
            SET ended_at = @EndedAt, updated_at = @Now
            WHERE platform_credential_id = @CredentialId AND ended_at IS NULL
            """,
            new { CredentialId = credentialId, EndedAt = endedAt, Now = now },
            transaction: tx);
    }

    public async Task<int> EndActiveSessionsForCredentialExceptGame(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        long credentialId, long gameId, DateTimeOffset endedAt,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await conn.ExecuteAsync(
            """
            UPDATE play_sessions
            SET ended_at = @EndedAt, updated_at = @Now
            WHERE platform_credential_id = @CredentialId
              AND ended_at IS NULL
              AND game_id <> @GameId
            """,
            new { CredentialId = credentialId, GameId = gameId, EndedAt = endedAt, Now = now },
            transaction: tx);
    }

    public async Task<int> EndTimedOutActiveSessionsForCredential(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        long credentialId, DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes,
        CancellationToken ct)
    {
        return await conn.ExecuteAsync(
            """
            UPDATE play_sessions
            SET ended_at = LEAST(@EndedAt, updated_at + (@TimeoutMinutes * INTERVAL '1 minute')),
                updated_at = NOW()
            WHERE platform_credential_id = @CredentialId
              AND ended_at IS NULL
              AND updated_at < @TimeoutBefore
            """,
            new { CredentialId = credentialId, TimeoutBefore = timeoutBefore, EndedAt = endedAt, TimeoutMinutes = timeoutMinutes },
            transaction: tx);
    }

    public async Task<ActiveSessionRow?> FetchActiveSessionForCredential(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        long credentialId,
        CancellationToken ct)
    {
        return await conn.QueryFirstOrDefaultAsync<ActiveSessionRow>(
            """
            SELECT id, game_id, started_at, updated_at
            FROM play_sessions
            WHERE platform_credential_id = @CredentialId AND ended_at IS NULL
            ORDER BY started_at DESC
            LIMIT 1
            """,
            new { CredentialId = credentialId },
            transaction: tx);
    }

    public async Task<bool> TouchActiveSession(
        NpgsqlConnection conn, NpgsqlTransaction tx,
        long sessionId, DateTimeOffset touchedAt,
        CancellationToken ct)
    {
        var affected = await conn.ExecuteAsync(
            "UPDATE play_sessions SET updated_at = @TouchedAt WHERE id = @Id AND ended_at IS NULL",
            new { Id = sessionId, TouchedAt = touchedAt },
            transaction: tx);
        return affected > 0;
    }

    public async Task<int> EndTimedOutActiveSessions(
        DateTimeOffset timeoutBefore, DateTimeOffset endedAt, int timeoutMinutes,
        CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(
            """
            UPDATE play_sessions ps
            SET ended_at = LEAST(@EndedAt, ps.updated_at + (@TimeoutMinutes * INTERVAL '1 minute')),
                updated_at = NOW()
            FROM platform_credentials pc
            JOIN platforms p ON p.id = pc.platform_id
            WHERE ps.platform_credential_id = pc.id
              AND p.key = 'playnite'
              AND ps.ended_at IS NULL
              AND ps.updated_at < @TimeoutBefore
            """,
            new { TimeoutBefore = timeoutBefore, EndedAt = endedAt, TimeoutMinutes = timeoutMinutes });
    }
}

public sealed class PlayniteCredentialRow
{
    public long Id { get; set; }
}

public sealed class ActiveSessionRow
{
    public long Id { get; set; }
    public long GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
