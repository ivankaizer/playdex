using Microsoft.EntityFrameworkCore;
using Npgsql;
using Api.Infrastructure.Persistence;
using Api.Infrastructure.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class GameExternalIdentitiesMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MigrationCreatesTypedIdentitiesAndBackfillsOnlyProvenProviders()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetUpLegacySchema(connection, transaction);
        await Execute(connection, transaction, """
            insert into games (title, external_id, canonical_key)
            values
                ('Steam Game', ' 10 ', 'Steam:10'),
                ('PSN Game', 'PPSA123', 'psn:PPSA123'),
                ('Xbox Game', '20', 'xbox:20'),
                ('IGDB Game', '30', 'igdb:30'),
                ('GOG Game', '40', 'gog:40'),
                ('Unknown Game', '50', 'unknown:50'),
                ('Missing ID', null, 'steam:missing'),
                ('Blank ID', '   ', 'steam:blank'),
                ('No Prefix Separator', '60', 'steam');
            """);

        await new GameExternalIdentitiesMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        await using var enumCommand = new NpgsqlCommand(
            """
            select enumlabel
            from pg_enum
            join pg_type on pg_type.oid = pg_enum.enumtypid
            where pg_type.typname = 'game_external_identity_type'
              and pg_type.typnamespace = current_schema()::regnamespace
            order by enumsortorder;
            """,
            connection,
            transaction);
        var enumValues = new List<string>();
        await using (var reader = await enumCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                enumValues.Add(reader.GetString(0));
            }
        }
        Assert.Equal(["steam", "psn", "xbox", "igdb"], enumValues);

        await using var identityCommand = new NpgsqlCommand(
            """
            select type::text, external_id, title
            from game_external_identities
            order by type::text;
            """,
            connection,
            transaction);
        var identities = new List<(string Type, string ExternalId, string Title)>();
        await using (var reader = await identityCommand.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                identities.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        Assert.Equal(4, identities.Count);
        Assert.Contains(("steam", "10", "Steam Game"), identities);
        Assert.Contains(("psn", "PPSA123", "PSN Game"), identities);
        Assert.Contains(("xbox", "20", "Xbox Game"), identities);
        Assert.Contains(("igdb", "30", "IGDB Game"), identities);

        await using var indexCommand = new NpgsqlCommand(
            """
            select count(*)
            from pg_indexes
            where schemaname = current_schema()
              and tablename = 'game_external_identities'
              and indexname = 'game_external_identities_game_id_idx';
            """,
            connection,
            transaction);
        Assert.Equal(1L, await indexCommand.ExecuteScalarAsync());
    }

    [Fact]
    public async Task MigrationConstraintsRejectInvalidDataAndReferencedGameDeletion()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetUpLegacySchema(connection, transaction);
        await Execute(connection, transaction, """
            insert into games (title, external_id, canonical_key)
            values ('Steam Game', '10', 'steam:10');
            """);
        await new GameExternalIdentitiesMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        await AssertDatabaseRejects(connection, transaction, """
            insert into game_external_identities (game_id, type, external_id, title)
            values (1, 'gog', '11', 'GOG Identity');
            """, PostgresErrorCodes.InvalidTextRepresentation);

        await AssertDatabaseRejects(connection, transaction, """
            insert into game_external_identities (game_id, type, external_id, title)
            values (1, 'steam', '  ', 'Blank Identity');
            """, PostgresErrorCodes.CheckViolation);

        await AssertDatabaseRejects(connection, transaction, """
            insert into game_external_identities (game_id, type, external_id, title)
            values (1, 'steam', '10', 'Duplicate Identity');
            """, PostgresErrorCodes.UniqueViolation);

        await AssertDatabaseRejects(
            connection,
            transaction,
            "delete from games where id = 1;",
            PostgresErrorCodes.ForeignKeyViolation);
    }

    [Fact]
    public async Task MigrationFailsWhenLegacyGamesShareAnIdentity()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetUpLegacySchema(connection, transaction);
        await Execute(connection, transaction, """
            insert into games (title, external_id, canonical_key)
            values
                ('First', ' 10 ', 'steam:10'),
                ('Second', '10', 'Steam:10-copy');
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            new GameExternalIdentitiesMigration().ApplyAsync(
                connection,
                transaction,
                CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Contains("Conflicting legacy Game external identities", exception.MessageText);
    }

    [Fact]
    public async Task EfMappingRoundTripsExternalIdentityEnum()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var schema = $"game_identity_{Guid.NewGuid():N}";
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await using var setupConnection = await postgres.DataSource!.OpenConnectionAsync();
        try
        {
            await Execute(setupConnection, null, $"create schema {quotedSchema};");
            await Execute(setupConnection, null, $"set search_path to {quotedSchema};");
            await Execute(setupConnection, null, LegacyGamesTableSql);
            await Execute(setupConnection, null, """
                insert into games (title, external_id, canonical_key)
                values ('Existing Game', null, 'unknown:existing');
                """);
            await using (var transaction = await setupConnection.BeginTransactionAsync())
            {
                await new GameExternalIdentitiesMigration().ApplyAsync(
                    setupConnection,
                    transaction,
                    CancellationToken.None);
                await transaction.CommitAsync();
            }

            var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
            var connectionString = new NpgsqlConnectionStringBuilder(
                PostgresConnectionString.Normalize(databaseUrl))
            {
                SearchPath = schema,
            }.ConnectionString;
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            await using var dataSource = dataSourceBuilder.Build();
            var options = new DbContextOptionsBuilder<PlaydexDbContext>()
                .UseNpgsql(
                    dataSource,
                    npgsql => npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type"))
                .Options;

            await using (var db = new PlaydexDbContext(options))
            {
                db.GameExternalIdentities.Add(new GameExternalIdentity
                {
                    GameId = 1,
                    Type = GameExternalIdentityType.Igdb,
                    ExternalId = "123",
                    Title = "Existing Game",
                });
                await db.SaveChangesAsync();
            }

            await using (var db = new PlaydexDbContext(options))
            {
                var identity = await db.GameExternalIdentities.AsNoTracking().SingleAsync();
                Assert.Equal(GameExternalIdentityType.Igdb, identity.Type);
                Assert.Equal("123", identity.ExternalId);
                Assert.Equal("Existing Game", identity.Title);
            }
        }
        finally
        {
            await Execute(setupConnection, null, "set search_path to public;");
            await Execute(setupConnection, null, $"drop schema if exists {quotedSchema} cascade;");
        }
    }

    [Fact]
    public async Task MappedDataSourceCanApplyMigrationBeforeEnumExists()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var schema = $"game_identity_{Guid.NewGuid():N}";
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await using var setupConnection = await postgres.DataSource!.OpenConnectionAsync();
        await Execute(setupConnection, null, $"create schema {quotedSchema};");
        try
        {
            await Execute(setupConnection, null, $"set search_path to {quotedSchema};");
            await Execute(setupConnection, null, LegacyGamesTableSql);

            var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
            var connectionString = new NpgsqlConnectionStringBuilder(
                PostgresConnectionString.Normalize(databaseUrl))
            {
                SearchPath = schema,
            }.ConnectionString;
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            await using var dataSource = dataSourceBuilder.Build();
            await using var mappedConnection = await dataSource.OpenConnectionAsync();
            await using var transaction = await mappedConnection.BeginTransactionAsync();

            await new GameExternalIdentitiesMigration().ApplyAsync(
                mappedConnection,
                transaction,
                CancellationToken.None);
            await transaction.CommitAsync();
            // MigrationRunner reloads the pooled type metadata after its migration batch.
            await mappedConnection.ReloadTypesAsync();

            await using var command = new NpgsqlCommand(
                "select 'steam'::game_external_identity_type::text;",
                mappedConnection);
            Assert.Equal("steam", await command.ExecuteScalarAsync());
        }
        finally
        {
            await Execute(setupConnection, null, "set search_path to public;");
            await Execute(setupConnection, null, $"drop schema if exists {quotedSchema} cascade;");
        }
    }

    private static async Task SetUpLegacySchema(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction)
    {
        var schema = $"game_identity_{Guid.NewGuid():N}";
        var quotedSchema = new NpgsqlCommandBuilder().QuoteIdentifier(schema);
        await Execute(connection, transaction, $"create schema {quotedSchema};");
        await Execute(connection, transaction, $"set local search_path to {quotedSchema};");
        await Execute(connection, transaction, LegacyGamesTableSql);
    }

    private static async Task AssertDatabaseRejects(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql,
        string expectedSqlState)
    {
        var savepoint = $"assert_{Guid.NewGuid():N}";
        await Execute(connection, transaction, $"savepoint {savepoint};");
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute(connection, transaction, sql));
        Assert.Equal(expectedSqlState, exception.SqlState);
        await Execute(connection, transaction, $"rollback to savepoint {savepoint};");
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private const string LegacyGamesTableSql = """
        create table games (
            id bigserial primary key,
            title text not null,
            external_id text,
            canonical_key text not null unique,
            created_at timestamptz not null default now(),
            updated_at timestamptz not null default now()
        );
        """;
}
