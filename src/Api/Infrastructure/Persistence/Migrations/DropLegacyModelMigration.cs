using Npgsql;

namespace Api.Infrastructure.Persistence.Migrations;

public sealed class DropLegacyModelMigration : IPlaydexMigration
{
    public string Id => "009_drop_legacy_model";
    public bool SupportsRollback => false;
    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        do $$
        begin
            if exists (select 1 from play_sessions where user_id is null or game_id is null or platform_id is null or capture_method is null) then
                raise exception 'Cannot drop legacy model: incomplete PlaySession target dimensions.';
            end if;
            if exists (select 1 from play_sessions where capture_method <> 'manual' and integration_id is null) then
                raise exception 'Cannot drop legacy model: automated session without Integration.';
            end if;
            if exists (select 1 from poll_results where integration_id is null) then
                raise exception 'Cannot drop legacy model: PollResult without Integration.';
            end if;
        end $$;

        drop trigger if exists play_sessions_populate_target_fields on play_sessions;
        drop function if exists populate_play_session_target_fields();
        alter table play_sessions drop column platform_credential_id;
        alter table poll_results drop column platform_credential_id, drop column game_id;
        drop table manual_game_inputs;
        drop table platform_credentials;
        alter table games drop column external_id, drop column canonical_key, drop column platform_id;
        update play_sessions
        set platform_id = (select id from platforms where key = 'unknown'), updated_at = now()
        where platform_id in (select id from platforms where key in ('manual','playnite'));
        delete from platforms where key in ('manual','playnite');
        insert into platforms(key,name,created_at,updated_at)
        values ('gog','GOG',now(),now()) on conflict(key) do nothing;
        """;
}
