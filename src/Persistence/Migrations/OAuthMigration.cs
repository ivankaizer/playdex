using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Playdex as an OAuth authorization server for assistants (MCP): the clients a user connected (grants), the
/// authorization codes of the consent flow and the grants' refresh tokens. Codes and tokens are stored as hashes.
/// </summary>
public sealed class OAuthMigration : IPlaydexMigration
{
    public string Id => "023_oauth";
    public bool SupportsRollback => true;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        create table if not exists oauth_grants (
            id uuid primary key,
            user_id bigint not null references users(id) on delete cascade,
            client_id text not null,
            client_name text not null,
            scope text not null,
            resource text not null,
            token_version integer not null,
            created_at timestamptz not null default now(),
            last_used_at timestamptz,
            revoked_at timestamptz
        );
        create index if not exists oauth_grants_user_id_idx on oauth_grants (user_id);

        create table if not exists oauth_authorization_codes (
            code_hash bytea primary key,
            user_id bigint not null references users(id) on delete cascade,
            client_id text not null,
            client_name text not null,
            redirect_uri text not null,
            code_challenge text not null,
            scope text not null,
            resource text not null,
            created_at timestamptz not null default now(),
            expires_at timestamptz not null,
            used_at timestamptz,
            grant_id uuid references oauth_grants(id) on delete cascade
        );
        create index if not exists oauth_authorization_codes_expires_at_idx on oauth_authorization_codes (expires_at);

        create table if not exists oauth_refresh_tokens (
            id bigserial primary key,
            grant_id uuid not null references oauth_grants(id) on delete cascade,
            token_hash bytea not null,
            created_at timestamptz not null default now(),
            expires_at timestamptz not null,
            rotated_at timestamptz,
            constraint oauth_refresh_tokens_token_hash_key unique (token_hash)
        );
        create index if not exists oauth_refresh_tokens_grant_id_idx on oauth_refresh_tokens (grant_id);
        """;
}
