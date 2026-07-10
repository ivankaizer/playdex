using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Playdex.Api.Data;
using Playdex.Api.Data.Migrations;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class MigrationCatalogTests
{
    [Fact]
    public void Constructor_OrdersMigrationsByImmutableIdentifier()
    {
        var catalog = new MigrationCatalog([new StubMigration("002_second"), new StubMigration("001_first")]);

        Assert.Equal(["001_first", "002_second"], catalog.Migrations.Select(migration => migration.Id));
    }

    [Theory]
    [InlineData("first")]
    [InlineData("01_first")]
    [InlineData("001-First")]
    public void Constructor_RejectsInvalidIdentifiers(string identifier)
    {
        Assert.Throws<InvalidOperationException>(() => new MigrationCatalog([new StubMigration(identifier)]));
    }

    [Fact]
    public void ValidateApplied_RejectsUnknownDatabaseHistory()
    {
        var catalog = new MigrationCatalog([new StubMigration("001_first")]);

        var exception = Assert.Throws<InvalidOperationException>(
            () => catalog.ValidateApplied(["999_unknown"]));

        Assert.Contains("999_unknown", exception.Message);
    }

    [Fact]
    public void ValidateApplied_RejectsHistoryWithAMissingPredecessor()
    {
        var catalog = new MigrationCatalog([new StubMigration("001_first"), new StubMigration("002_second")]);

        Assert.Throws<InvalidOperationException>(() => catalog.ValidateApplied(["002_second"]));
    }

    private sealed class StubMigration(string id) : IPlaydexMigration
    {
        public string Id { get; } = id;
        public bool SupportsRollback => false;

        public Task ApplyAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

public sealed class MigrationRunnerTests
{
    [Fact]
    public async Task RunAsync_AppliesEachMigrationExactlyOnce()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var migration = new CountingMigration("001_test_probe");
        var runner = new MigrationRunner(
            dataSource,
            new MigrationCatalog([migration]),
            NullLogger<MigrationRunner>.Instance);
        var competingRunner = new MigrationRunner(
            dataSource,
            new MigrationCatalog([migration]),
            NullLogger<MigrationRunner>.Instance);

        try
        {
            await Task.WhenAll(runner.RunAsync(), competingRunner.RunAsync());
            await runner.RunAsync();

            Assert.Equal(1, migration.ApplyCount);
        }
        finally
        {
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var cleanup = new NpgsqlCommand(
                "delete from dotnet_schema_migrations where migration_id = '001_test_probe'",
                connection);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private sealed class CountingMigration(string id) : IPlaydexMigration
    {
        public string Id { get; } = id;
        public bool SupportsRollback => false;
        public int ApplyCount { get; private set; }

        public async Task ApplyAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CancellationToken cancellationToken)
        {
            ApplyCount++;
            await using var command = new NpgsqlCommand("select 1", connection, transaction);
            await command.ExecuteScalarAsync(cancellationToken);
        }
    }
}
