using Npgsql;

namespace Api.Infrastructure.Persistence.Migrations;

public sealed class PollingTargetModelMigration : IPlaydexMigration
{
    public string Id => "007_polling_target_model";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        alter table poll_results
            add column integration_id bigint,
            add column game_external_identity_id bigint;

        update poll_results pr
        set integration_id = i.id,
            game_external_identity_id = (
                select gei.id
                from game_external_identities gei
                where gei.game_id = pr.game_id
                  and gei.type::text = i.type::text
                order by gei.id
                limit 1
            )
        from integrations i
        where i.id = pr.platform_credential_id
          and i.type <> 'api';

        do $$
        begin
            if exists (select 1 from poll_results where integration_id is null) then
                raise exception 'PollResult backfill found a result without polling Integration.';
            end if;
        end
        $$;

        alter table poll_results
            alter column integration_id set not null,
            add constraint poll_results_integration_id_fkey foreign key (integration_id) references integrations(id) on delete restrict,
            add constraint poll_results_game_external_identity_id_fkey foreign key (game_external_identity_id) references game_external_identities(id) on delete set null;

        create index poll_results_integration_id_idx on poll_results (integration_id);
        create index poll_results_game_external_identity_id_idx on poll_results (game_external_identity_id);
        """;
}
