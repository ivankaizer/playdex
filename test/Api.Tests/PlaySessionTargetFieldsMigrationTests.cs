using Npgsql;
using Shared.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PlaySessionTargetFieldsMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PlayniteSessionBackfillUsesPcAsCapturedPlatform()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var schema = $"session_target_{Guid.NewGuid():N}";
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await Execute(connection, transaction, $"create schema {quotedSchema};");
        await Execute(connection, transaction, $"set local search_path to {quotedSchema};");
        await Execute(connection, transaction, LegacySchemaSql);
        await Execute(connection, transaction, """
            insert into users (id, email) values (1, 'playnite@example.test');
            insert into platforms (id, key, name) values
                (1, 'pc', 'PC'),
                (2, 'steam', 'Steam'),
                (3, 'playnite', 'Playnite');
            insert into platform_credentials (id, user_id, platform_id, api_key)
            values (10, 1, 3, 'playnite-key');
            insert into games (id, title, canonical_key, platform_id)
            values (20, 'Steam-catalogued game', 'steam:20', 2);
            insert into play_sessions (id, platform_credential_id, game_id, started_at)
            values (30, 10, 20, now());
            """);

        await new IntegrationsMigration().ApplyAsync(
            connection,
            transaction,
            CancellationToken.None);
        await new PlaySessionTargetFieldsMigration().ApplyAsync(
            connection,
            transaction,
            CancellationToken.None);

        await using var command = new NpgsqlCommand("""
            select ps.capture_method::text, p.key
            from play_sessions ps
            join platforms p on p.id = ps.platform_id
            where ps.id = 30;
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("api", reader.GetString(0));
        Assert.Equal("pc", reader.GetString(1));
    }

    [Fact]
    public async Task CorrectiveMigrationChangesOnlyApiSessionsCreatedBeforeLegacyRemoval()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var schema = $"session_repair_{Guid.NewGuid():N}";
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await Execute(connection, transaction, $"create schema {quotedSchema};");
        await Execute(connection, transaction, $"set local search_path to {quotedSchema};");
        await Execute(connection, transaction, """
            create type integration_type as enum ('steam', 'psn', 'xbox', 'api');
            create type capture_method as enum ('polling', 'api', 'manual');
            create table platforms (id bigint primary key, key text not null unique);
            create table integrations (id bigint primary key, type integration_type not null);
            create table play_sessions (
                id bigint primary key,
                integration_id bigint not null,
                platform_id bigint not null,
                capture_method capture_method not null,
                created_at timestamptz not null,
                updated_at timestamptz not null
            );
            create table dotnet_schema_migrations (
                migration_id text primary key,
                applied_at timestamptz not null,
                supports_rollback boolean not null
            );
            insert into platforms (id, key) values (1, 'pc'), (2, 'steam');
            insert into integrations (id, type) values (10, 'api');
            insert into dotnet_schema_migrations (migration_id, applied_at, supports_rollback)
            values ('009_drop_legacy_model', '2026-08-13T10:00:00Z', false);
            insert into play_sessions (
                id, integration_id, platform_id, capture_method, created_at, updated_at)
            values
                (20, 10, 2, 'api', '2026-08-12T10:00:00Z', '2026-08-12T10:00:00Z'),
                (21, 10, 2, 'api', '2026-08-13T11:00:00Z', '2026-08-13T11:00:00Z');
            """);

        await new CorrectLegacyApiSessionPlatformsMigration().ApplyAsync(
            connection,
            transaction,
            CancellationToken.None);

        await using var command = new NpgsqlCommand("""
            select p.key
            from play_sessions ps
            join platforms p on p.id = ps.platform_id
            order by ps.id;
            """, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("pc", reader.GetString(0));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("steam", reader.GetString(0));
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private const string LegacySchemaSql = """
        create table users (
            id bigint primary key,
            email text not null,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );
        create table platforms (
            id bigint primary key,
            key text not null unique,
            name text not null,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );
        create table platform_credentials (
            id bigint primary key,
            user_id bigint not null references users(id),
            platform_id bigint not null references platforms(id),
            api_key text,
            account_id text,
            api_key_locked boolean not null default false,
            active boolean not null default true,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );
        create table games (
            id bigint primary key,
            title text not null,
            external_id text,
            canonical_key text not null unique,
            platform_id bigint not null references platforms(id),
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );
        create table play_sessions (
            id bigint primary key,
            platform_credential_id bigint not null references platform_credentials(id),
            game_id bigint not null references games(id),
            started_at timestamptz not null,
            ended_at timestamptz,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );
        """;
}
