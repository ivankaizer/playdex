using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Creates the current Playdex schema for new .NET deployments and records an
/// idempotent baseline when adopting a database already migrated by Kysely.
/// </summary>
public sealed class CurrentSchemaMigration : IPlaydexMigration
{
    public string Id => "001_current_schema";
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
        create table if not exists users (
            id bigserial primary key,
            email text not null unique,
            password text,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            chat_id text,
            is_admin boolean not null default false
        );

        create unique index if not exists users_chat_id_unique
            on users (chat_id) where chat_id is not null;

        create table if not exists platforms (
            id bigserial primary key,
            key text not null unique,
            name text not null,
            igdb_platform_id integer,
            parent_id bigint references platforms(id) on delete set null,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );

        insert into platforms (key, name, igdb_platform_id)
        values
            ('ps5', 'PS5', 167),
            ('xbox-series-xs', 'Xbox Series X|S', 169),
            ('pc', 'PC', 6),
            ('switch-1', 'Nintendo Switch 1', 130),
            ('switch-2', 'Nintendo Switch 2', 508),
            ('unknown', 'Unknown', null)
        on conflict (key) do nothing;

        insert into platforms (key, name, parent_id)
        values
            ('steam', 'Steam', (select id from platforms where key = 'pc')),
            ('playnite', 'Playnite', (select id from platforms where key = 'pc')),
            ('psn', 'PSN', (select id from platforms where key = 'ps5')),
            ('manual', 'Manual', null)
        on conflict (key) do nothing;

        create table if not exists platform_credentials (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            api_key text,
            account_id text,
            api_key_locked boolean not null default false,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            active boolean not null default true,
            platform_id bigint not null references platforms(id),
            constraint platform_credentials_user_api_account_key unique (user_id, api_key, account_id)
        );

        create table if not exists games (
            id bigserial primary key,
            title text not null,
            external_id text,
            canonical_key text not null unique,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            capsule_url text,
            platform_id bigint not null references platforms(id),
            capsule_url_locked boolean not null default false,
            title_locked boolean not null default false
        );

        create table if not exists play_sessions (
            id bigserial primary key,
            platform_credential_id bigint not null references platform_credentials(id) on delete cascade,
            game_id bigint not null references games(id) on delete cascade,
            started_at timestamptz not null,
            ended_at timestamptz,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );

        create table if not exists poll_results (
            id bigserial primary key,
            platform_credential_id bigint not null references platform_credentials(id) on delete cascade,
            game_id bigint references games(id) on delete set null,
            polled_at timestamptz not null,
            raw_payload jsonb,
            created_at timestamptz not null default now()
        );

        create index if not exists poll_results_platform_credential_id_idx
            on poll_results (platform_credential_id);
        create index if not exists poll_results_polled_at_idx
            on poll_results (polled_at);

        create table if not exists manual_game_inputs (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            raw_title text not null,
            canonical_title text not null,
            mapped_game_id bigint references games(id) on delete set null,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );

        create unique index if not exists manual_game_inputs_user_title_idx
            on manual_game_inputs (user_id, canonical_title);
        create index if not exists manual_game_inputs_mapped_game_id_idx
            on manual_game_inputs (mapped_game_id);

        create table if not exists ignored_games (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            game_id bigint not null references games(id) on delete cascade,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            constraint ignored_games_user_id_game_id_key unique (user_id, game_id)
        );

        create index if not exists ignored_games_user_id_idx on ignored_games (user_id);

        create table if not exists game_ratings (
            id bigserial primary key,
            user_id bigint not null references users(id) on delete cascade,
            game_id bigint not null references games(id) on delete cascade,
            rating smallint not null,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            constraint game_ratings_user_id_game_id_key unique (user_id, game_id),
            constraint game_ratings_rating_check check (rating >= 1 and rating <= 5)
        );

        create index if not exists game_ratings_user_id_idx on game_ratings (user_id);
        create index if not exists game_ratings_game_id_idx on game_ratings (game_id);

        create table if not exists user_settings (
            user_id bigint primary key references users(id) on delete cascade,
            ignore_short_sessions_minutes integer not null default 0,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );

        create table if not exists game_connections (
            user_id bigint not null references users(id) on delete cascade,
            game_id bigint not null references games(id) on delete cascade,
            connected_game_id bigint not null references games(id) on delete cascade,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now(),
            constraint game_connections_pkey primary key (user_id, game_id, connected_game_id),
            constraint game_connections_check check (game_id < connected_game_id)
        );

        create index if not exists game_connections_user_id_idx on game_connections (user_id);
        """;
}
