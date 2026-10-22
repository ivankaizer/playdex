using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Stores issued refresh tokens (hashed) so each one can be used once, and makes email addresses unique regardless
/// of case, since sign-in now matches them that way. Databases where two accounts differ only in the case of their
/// email are refused rather than changed: the accounts have to be merged or renamed by hand first.
/// </summary>
public sealed class RefreshTokensMigration : IPlaydexMigration
{
    public string Id => "016_refresh_tokens";
    public bool SupportsRollback => true;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        create table if not exists refresh_tokens (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            family_id uuid not null,
            token_hash bytea not null,
            created_at timestamptz not null default now(),
            expires_at timestamptz not null,
            rotated_at timestamptz,
            constraint refresh_tokens_token_hash_key unique (token_hash)
        );

        create index if not exists refresh_tokens_user_id_idx on refresh_tokens (user_id);
        create index if not exists refresh_tokens_family_id_idx on refresh_tokens (family_id);
        """;
}
