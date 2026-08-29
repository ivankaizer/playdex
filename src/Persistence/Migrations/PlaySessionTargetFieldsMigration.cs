using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>Materializes the authoritative dimensions of every PlaySession.</summary>
public sealed class PlaySessionTargetFieldsMigration : IPlaydexMigration
{
    public string Id => "006_play_session_target_fields";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        create type capture_method as enum ('polling', 'api', 'manual');

        alter table play_sessions
            add column user_id bigint,
            add column platform_id bigint,
            add column capture_method capture_method,
            add column integration_id bigint;

        update play_sessions ps
        set user_id = pc.user_id,
            platform_id = case
                when cp.key in ('steam', 'psn', 'xbox') then pc.platform_id
                when cp.key = 'playnite' then (select id from platforms where key = 'pc')
                else g.platform_id
            end,
            capture_method = case
                when cp.key = 'manual' then 'manual'::capture_method
                when cp.key in ('playnite', 'custom', 'pc') then 'api'::capture_method
                when cp.key in ('steam', 'psn', 'xbox') then 'polling'::capture_method
            end,
            integration_id = case when cp.key = 'manual' then null else i.id end
        from platform_credentials pc
        join platforms cp on cp.id = pc.platform_id
        left join integrations i on i.id = pc.id
        cross join games g
        where pc.id = ps.platform_credential_id
          and g.id = ps.game_id;

        do $$
        begin
            if exists (
                select 1 from play_sessions
                where user_id is null or platform_id is null or capture_method is null
                   or (capture_method <> 'manual' and integration_id is null)
                   or (capture_method = 'manual' and integration_id is not null)
            ) then
                raise exception 'PlaySession target-field backfill left unresolved rows.';
            end if;
        end
        $$;

        alter table play_sessions
            alter column user_id set not null,
            alter column platform_id set not null,
            alter column capture_method set not null,
            add constraint play_sessions_user_id_fkey foreign key (user_id) references users(id) on delete restrict,
            add constraint play_sessions_platform_id_fkey foreign key (platform_id) references platforms(id) on delete restrict,
            add constraint play_sessions_integration_id_fkey foreign key (integration_id) references integrations(id) on delete restrict,
            add constraint play_sessions_manual_integration_check check (
                (capture_method = 'manual' and integration_id is null)
                or (capture_method <> 'manual' and integration_id is not null)
            );

        create index play_sessions_user_id_idx on play_sessions (user_id);
        create index play_sessions_platform_id_idx on play_sessions (platform_id);
        create index play_sessions_integration_id_idx on play_sessions (integration_id);

        create function populate_play_session_target_fields() returns trigger language plpgsql as $$
        declare
            credential_user_id bigint;
            credential_platform_id bigint;
            credential_platform_key text;
            selected_game_platform_id bigint;
        begin
            if new.user_id is not null and new.platform_id is not null and new.capture_method is not null then
                return new;
            end if;

            select pc.user_id, pc.platform_id, p.key
            into strict credential_user_id, credential_platform_id, credential_platform_key
            from platform_credentials pc
            join platforms p on p.id = pc.platform_id
            where pc.id = new.platform_credential_id;

            select platform_id into strict selected_game_platform_id from games where id = new.game_id;
            new.user_id := credential_user_id;

            if credential_platform_key = 'manual' then
                new.capture_method := 'manual';
                new.platform_id := selected_game_platform_id;
                new.integration_id := null;
            elsif credential_platform_key in ('playnite', 'custom', 'pc') then
                new.capture_method := 'api';
                new.platform_id := case
                    when credential_platform_key = 'playnite'
                        then (select id from platforms where key = 'pc')
                    else selected_game_platform_id
                end;
                new.integration_id := new.platform_credential_id;
            elsif credential_platform_key in ('steam', 'psn', 'xbox') then
                new.capture_method := 'polling';
                new.platform_id := credential_platform_id;
                new.integration_id := new.platform_credential_id;
            else
                raise exception 'Unsupported legacy session credential platform: %', credential_platform_key;
            end if;
            return new;
        end
        $$;

        create trigger play_sessions_populate_target_fields
            before insert or update of platform_credential_id, game_id
            on play_sessions for each row
            execute function populate_play_session_target_fields();
        """;
}
