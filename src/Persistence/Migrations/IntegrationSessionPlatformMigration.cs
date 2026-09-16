using Npgsql;

namespace Playdex.Persistence.Migrations;

public sealed class IntegrationSessionPlatformMigration : IPlaydexMigration
{
    public string Id => "012_integration_session_platform";
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
        -- Older Kysely baselines can have the right columns without the
        -- identity constraints required by later foreign keys and upserts.
        do $$
        begin
            if not exists (
                select 1
                from pg_constraint
                where conrelid = 'platforms'::regclass
                  and contype = 'p'
            ) then
                alter table platforms add constraint platforms_pkey primary key (id);
            end if;
        end
        $$;

        create unique index if not exists platforms_key_unique on platforms (key);

        alter table platforms
            alter column created_at set default now(),
            alter column updated_at set default now();

        alter table integrations
            add column if not exists session_platform_id bigint references platforms(id) on delete no action;

        create index if not exists integrations_session_platform_id_idx
            on integrations (session_platform_id);

        insert into platforms (key, name, igdb_platform_id)
        values
            ('ps3', 'PS3', 9),
            ('ps4', 'PS4', 48),
            ('ps5', 'PS5', 167)
        on conflict (key) do update set
            name = excluded.name,
            igdb_platform_id = excluded.igdb_platform_id,
            updated_at = now();

        update platforms
        set parent_id = null,
            updated_at = now()
        where key = 'psn'
          and parent_id is not null;

        update platforms
        set parent_id = (select id from platforms where key = 'psn'),
            updated_at = now()
        where key in ('ps3', 'ps4', 'ps5')
          and parent_id is distinct from (select id from platforms where key = 'psn');
        """;
}
