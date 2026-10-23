using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Indexes for the hot read paths (sessions by game and date, open and recently ended sessions, the last poll of an
/// integration, title search) and constraints that turn silent data loss or duplicates into errors: deleting a game
/// that still has sessions now fails instead of erasing them, and a user can connect a given account only once per
/// integration type.
/// </summary>
public sealed class IndexesAndConstraintsMigration : IPlaydexMigration
{
    public const string DuplicateIntegrationsMessage = "integrations has duplicate (user_id, type, account_id) rows";

    public string Id => "019_indexes_and_constraints";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = $$"""
        create index if not exists play_sessions_game_id_idx on play_sessions (game_id);
        create index if not exists play_sessions_started_at_idx on play_sessions (started_at);
        create index if not exists play_sessions_open_user_id_idx on play_sessions (user_id) where ended_at is null;
        create index if not exists play_sessions_ended_at_idx on play_sessions (ended_at desc) where ended_at is not null;
        create index if not exists ai_generations_user_id_idx on ai_generations (user_id);

        -- The last poll of an integration is one index probe; the single-column index is a prefix of this one.
        create index if not exists poll_results_integration_id_polled_at_idx
            on poll_results (integration_id, polled_at desc);
        drop index if exists poll_results_integration_id_idx;

        -- Title search uses ILIKE '%term%'. pg_trgm is a trusted extension, but a role without CREATE on the
        -- database (or a server without contrib) cannot install it; search then keeps working without the index.
        do $$
        begin
            create extension if not exists pg_trgm;
        exception when others then
            raise notice 'pg_trgm is not installed (%); games.title search stays unindexed.', sqlerrm;
        end
        $$;

        do $$
        begin
            if exists (select 1 from pg_extension where extname = 'pg_trgm') then
                execute 'create index if not exists games_title_trgm_idx on games using gin (title gin_trgm_ops)';
            end if;
        end
        $$;

        -- A merge or cleanup bug must fail rather than erase play history. Older baselines may name the
        -- constraint differently, so drop whichever foreign key covers play_sessions.game_id.
        do $$
        declare
            constraint_name text;
        begin
            for constraint_name in
                select con.conname
                from pg_constraint con
                where con.conrelid = 'play_sessions'::regclass
                  and con.contype = 'f'
                  and con.confrelid = 'games'::regclass
                  and con.conkey = array[(
                      select attnum from pg_attribute
                      where attrelid = 'play_sessions'::regclass and attname = 'game_id')]::smallint[]
            loop
                execute format('alter table play_sessions drop constraint %I', constraint_name);
            end loop;
        end
        $$;

        alter table play_sessions
            add constraint play_sessions_game_id_fkey
            foreign key (game_id) references games(id) on delete restrict;

        -- One polling integration per user, type and account: the same Steam ID added three times was polled three
        -- times. API integrations are excluded: their account_id is a fixed placeholder, not an account. Existing
        -- duplicates are not deleted here; the migration stops and names them instead.
        do $$
        declare
            duplicate record;
        begin
            select user_id, type, account_id, string_agg(id::text, ', ' order by id) as ids
            into duplicate
            from integrations
            where type <> 'api' and account_id is not null
            group by user_id, type, account_id
            having count(*) > 1
            order by user_id, type, account_id
            limit 1;

            if found then
                raise exception '{{DuplicateIntegrationsMessage}}: user %, type %, account % (integration ids %).',
                    duplicate.user_id, duplicate.type, duplicate.account_id, duplicate.ids
                    using hint = 'Delete or change the account of all but one of these integrations (and repoint '
                        || 'their play_sessions and poll_results), then restart. Find every duplicate with: '
                        || 'select user_id, type, account_id, array_agg(id) from integrations '
                        || 'where type <> ''api'' and account_id is not null group by 1, 2, 3 having count(*) > 1;';
            end if;
        end
        $$;

        create unique index if not exists integrations_user_type_account_unique
            on integrations (user_id, type, account_id) where type <> 'api';

        -- platforms.key is unique twice on databases created by 001 (platforms_key_key) and later given
        -- platforms_key_unique by 012. Keep the second one only where it is the sole unique index on key.
        do $$
        begin
            if exists (
                select 1
                from pg_index i
                join pg_class c on c.oid = i.indexrelid
                where i.indrelid = 'platforms'::regclass
                  and i.indisunique
                  and i.indpred is null
                  and i.indnatts = 1
                  and i.indkey[0] = (
                      select attnum from pg_attribute
                      where attrelid = 'platforms'::regclass and attname = 'key')
                  and c.relname <> 'platforms_key_unique'
            ) then
                drop index if exists platforms_key_unique;
            end if;
        end
        $$;
        """;
}
