using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Playdex.Persistence;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class MigrationRunnerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ExistingLedgerCanBeReadDuringStartupWithoutWaitingForExclusiveLock()
    {
        if (!postgres.IsConfigured) return;

        await using var scope = await LedgerScope.CreateAsync(postgres.DataSource!);
        var runner = CreateRunner(scope.DataSource, []);
        await runner.RunAsync();

        await using var reader = await scope.DataSource.OpenConnectionAsync();
        await using var transaction = await reader.BeginTransactionAsync();
        await using var read = new NpgsqlCommand("select count(*) from dotnet_schema_migrations", reader, transaction);
        await read.ExecuteScalarAsync();

        // A reader holds an ACCESS SHARE lock until its transaction ends.
        // Startup must not attempt unnecessary ALTER TABLE against this ledger.
        await runner.RunAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NewAndLegacyLedgersRecordAppliedTimestamp(bool legacyLedger)
    {
        if (!postgres.IsConfigured) return;

        await using var scope = await LedgerScope.CreateAsync(postgres.DataSource!);
        if (legacyLedger)
        {
            await using var legacy = scope.DataSource.CreateCommand("""
                create table dotnet_schema_migrations (
                    migration_id text primary key,
                    applied_at timestamptz not null,
                    supports_rollback boolean not null
                )
                """);
            await legacy.ExecuteNonQueryAsync();
        }

        var runner = CreateRunner(scope.DataSource, [new EmptyMigration()]);
        await runner.RunAsync();
        await runner.RunAsync();

        await using var query = scope.DataSource.CreateCommand("""
            select migration_id, applied_at, supports_rollback from dotnet_schema_migrations
            """);
        await using var reader = await query.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("001_test", reader.GetString(0));
        Assert.False(reader.IsDBNull(1));
        Assert.False(reader.GetBoolean(2));
        Assert.False(await reader.ReadAsync());
    }

    private static MigrationRunner CreateRunner(NpgsqlDataSource dataSource, IPlaydexMigration[] migrations) =>
        new(dataSource, new MigrationCatalog(migrations), NullLogger<MigrationRunner>.Instance);

    private sealed class EmptyMigration : IPlaydexMigration
    {
        public string Id => "001_test";
        public bool SupportsRollback => false;
        public Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class LedgerScope(NpgsqlDataSource root, string schema, NpgsqlDataSource dataSource) : IAsyncDisposable
    {
        public NpgsqlDataSource DataSource { get; } = dataSource;

        public static async Task<LedgerScope> CreateAsync(NpgsqlDataSource root)
        {
            var schema = $"migration_runner_{Guid.NewGuid():n}";
            await using var create = root.CreateCommand($"create schema {schema}");
            await create.ExecuteNonQueryAsync();
            var settings = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(
                Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!))
            {
                SearchPath = $"{schema},pg_catalog",
                CommandTimeout = 5,
                Options = "-c lock_timeout=1000",
            };
            return new LedgerScope(root, schema, NpgsqlDataSource.Create(settings.ConnectionString));
        }

        public async ValueTask DisposeAsync()
        {
            await DataSource.DisposeAsync();
            await using var drop = root.CreateCommand($"drop schema {schema} cascade");
            await drop.ExecuteNonQueryAsync();
        }
    }
}
