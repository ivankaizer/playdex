using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>Adds target Integration ownership without switching legacy runtime paths.</summary>
public sealed class IntegrationsMigration : IPlaydexMigration
{
    public string Id => "005_integrations";
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
        create type integration_type as enum ('steam', 'psn', 'xbox', 'api');

        create table integrations (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            type integration_type not null,
            platform_id bigint references platforms(id) on delete restrict,
            api_key text,
            account_id text,
            api_key_locked boolean not null default false,
            active boolean not null default true,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            constraint integrations_platform_ownership_check check (
                (type = 'api' and platform_id is null)
                or (type <> 'api' and platform_id is not null)
            )
        );

        create index integrations_user_id_idx on integrations (user_id);
        create index integrations_platform_id_idx on integrations (platform_id);
        create index integrations_api_key_idx on integrations (api_key)
            where api_key is not null;

        do $$
        begin
            if exists (
                select 1
                from platform_credentials pc
                join platforms p on p.id = pc.platform_id
                where pc.active
                  and p.key not in ('steam', 'psn', 'xbox', 'playnite', 'custom', 'pc', 'manual')
            ) then
                raise exception 'Unsupported active platform credential cannot be migrated to Integration.';
            end if;
        end
        $$;

        insert into integrations (
            id, user_id, type, platform_id, api_key, account_id,
            api_key_locked, active, created_at, updated_at)
        select
            pc.id,
            pc.user_id,
            case
                when p.key in ('playnite', 'custom', 'pc') then 'api'::integration_type
                else p.key::integration_type
            end,
            case when p.key in ('playnite', 'custom', 'pc') then null else pc.platform_id end,
            pc.api_key,
            pc.account_id,
            pc.api_key_locked,
            pc.active,
            pc.created_at,
            pc.updated_at
        from platform_credentials pc
        join platforms p on p.id = pc.platform_id
        where p.key in ('steam', 'psn', 'xbox', 'playnite', 'custom', 'pc');

        select setval(
            pg_get_serial_sequence('integrations', 'id'),
            greatest(coalesce((select max(id) from integrations), 0), 1),
            exists(select 1 from integrations));
        """;
}
