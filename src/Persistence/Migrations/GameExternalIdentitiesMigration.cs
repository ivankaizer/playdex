using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Introduces typed external Game identities and backfills identities that can
/// be derived from legacy Game columns without guessing.
/// </summary>
public sealed class GameExternalIdentitiesMigration : IPlaydexMigration
{
    public string Id => "004_game_external_identities";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        create type game_external_identity_type as enum ('steam', 'psn', 'xbox', 'igdb');

        create table game_external_identities (
            id bigserial primary key,
            game_id bigint not null references games(id) on delete restrict,
            type game_external_identity_type not null,
            external_id text not null,
            title text not null,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            constraint game_external_identities_type_external_id_key unique (type, external_id),
            constraint game_external_identities_external_id_check
                check (external_id = btrim(external_id) and external_id <> '')
        );

        create index game_external_identities_game_id_idx
            on game_external_identities (game_id);

        do $$
        begin
            if exists (
                select 1
                from games
                where external_id is not null
                  and btrim(external_id) <> ''
                  and position(':' in canonical_key) > 0
                  and lower(split_part(canonical_key, ':', 1)) in ('steam', 'psn', 'xbox', 'igdb')
                group by lower(split_part(canonical_key, ':', 1)), btrim(external_id)
                having count(*) > 1
            ) then
                raise exception 'Conflicting legacy Game external identities detected.'
                    using errcode = 'unique_violation';
            end if;
        end
        $$;

        insert into game_external_identities
            (game_id, type, external_id, title, created_at, updated_at)
        select
            id,
            lower(split_part(canonical_key, ':', 1))::game_external_identity_type,
            btrim(external_id),
            title,
            created_at,
            updated_at
        from games
        where external_id is not null
          and btrim(external_id) <> ''
          and position(':' in canonical_key) > 0
          and lower(split_part(canonical_key, ':', 1)) in ('steam', 'psn', 'xbox', 'igdb');
        """;
}
