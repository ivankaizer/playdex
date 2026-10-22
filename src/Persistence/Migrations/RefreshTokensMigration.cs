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

        do $$
        declare
            duplicates text;
        begin
            select string_agg(address, ', ' order by address)
            into duplicates
            from (
                select lower(email) as address
                from users
                group by lower(email)
                having count(*) > 1
            ) shared;

            if duplicates is not null then
                raise exception 'Several accounts share an email address that differs only in case: %. Sign-in now ignores case, so merge or rename these accounts, then start Playdex again.', duplicates
                    using errcode = 'unique_violation';
            end if;
        end
        $$;

        create unique index if not exists users_email_lower_unique on users (lower(email));
        """;
}
