using Npgsql;

namespace Shared.Persistence.Migrations;

public sealed class ManualTargetModelMigration : IPlaydexMigration
{
    public string Id => "008_manual_target_model";
    public bool SupportsRollback => false;
    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        alter table play_sessions alter column platform_credential_id drop not null;
        alter table play_sessions drop constraint if exists play_sessions_platform_credential_id_fkey;
        alter table play_sessions add constraint play_sessions_platform_credential_id_fkey
            foreign key (platform_credential_id) references platform_credentials(id) on delete restrict;

        create or replace function populate_play_session_target_fields() returns trigger language plpgsql as $$
        declare credential_user_id bigint; credential_platform_id bigint; credential_platform_key text; selected_game_platform_id bigint;
        begin
            if new.user_id is not null and new.platform_id is not null and new.capture_method is not null then return new; end if;
            if new.platform_credential_id is null then raise exception 'Target session fields are required without legacy credential.'; end if;
            select pc.user_id, pc.platform_id, p.key into strict credential_user_id, credential_platform_id, credential_platform_key
              from platform_credentials pc join platforms p on p.id=pc.platform_id where pc.id=new.platform_credential_id;
            select platform_id into strict selected_game_platform_id from games where id=new.game_id;
            new.user_id:=credential_user_id;
            if credential_platform_key='manual' then new.capture_method:='manual'; new.platform_id:=selected_game_platform_id; new.integration_id:=null;
            elsif credential_platform_key in ('playnite','custom','pc') then
                new.capture_method:='api';
                new.platform_id:=case when credential_platform_key='playnite' then (select id from platforms where key='pc') else selected_game_platform_id end;
                new.integration_id:=new.platform_credential_id;
            else new.capture_method:='polling'; new.platform_id:=credential_platform_id; new.integration_id:=new.platform_credential_id; end if;
            return new;
        end $$;
        """;
}
