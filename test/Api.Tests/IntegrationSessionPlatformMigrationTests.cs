using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class IntegrationSessionPlatformMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MigrationCreatesOverrideAndCompletePsnPlatformTree()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction, "drop table if exists integrations cascade; drop table if exists platforms cascade;");
        await Execute(connection, transaction, """
            create table platforms (
                id bigserial primary key,
                key text not null unique,
                name text not null,
                igdb_platform_id integer,
                parent_id bigint references platforms(id),
                created_at timestamptz not null default now(),
                updated_at timestamptz not null default now()
            );
            insert into platforms (key, name) values ('psn', 'PSN');
            create table integrations (id bigserial primary key);
            """);

        await new IntegrationSessionPlatformMigration().ApplyAsync(
            connection, transaction, CancellationToken.None);

        await using var columnCommand = new NpgsqlCommand(
            """
            select is_nullable
            from information_schema.columns
            where table_schema = current_schema()
              and table_name = 'integrations'
              and column_name = 'session_platform_id';
            """,
            connection,
            transaction);
        Assert.Equal("YES", await columnCommand.ExecuteScalarAsync());

        await using var platformsCommand = new NpgsqlCommand(
            """
            select child.key
            from platforms child
            join platforms parent on parent.id = child.parent_id
            where parent.key = 'psn'
            order by child.key;
            """,
            connection,
            transaction);
        var childKeys = new List<string>();
        await using var reader = await platformsCommand.ExecuteReaderAsync();
        while (await reader.ReadAsync()) childKeys.Add(reader.GetString(0));

        Assert.Equal(["ps3", "ps4", "ps5"], childKeys);
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
