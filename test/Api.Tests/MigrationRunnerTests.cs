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
        if (!postgres.IsConfigured)
        {
            return;
        }

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
        if (!postgres.IsConfigured)
        {
            return;
        }

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

    [Fact]
    public async Task LedgerIsReadInOrdinalOrderWhateverTheDatabaseCollation()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await LedgerScope.CreateAsync(postgres.DataSource!);
        // glibc collations skip '_' at the first level, so "004_ab" sorts before "004_a_z"; ordinally it is after.
        await using var find = scope.DataSource.CreateCommand(
            "select collname from pg_collation where collname in ('en_US.utf8', 'en_US.UTF-8', 'en_US') limit 1");
        if (await find.ExecuteScalarAsync() is not string collation)
        {
            return;
        }

        await using var probe = scope.DataSource.CreateCommand($"""select '004_ab' < '004_a_z' collate "{collation}" """);
        if (await probe.ExecuteScalarAsync() is not true)
        {
            return;
        }

        await using var ledger = scope.DataSource.CreateCommand($"""
            create table dotnet_schema_migrations (
                migration_id text collate "{collation}" primary key,
                applied_at timestamptz not null default now(),
                supports_rollback boolean not null)
            """);
        await ledger.ExecuteNonQueryAsync();

        IPlaydexMigration[] migrations = [new EmptyMigration("004_a_z"), new EmptyMigration("004_ab")];
        await CreateRunner(scope.DataSource, migrations).RunAsync();

        // Re-reading the ledger used to fail with "out of order" on such collations.
        await CreateRunner(scope.DataSource, migrations).RunAsync();
    }

    [Fact]
    public async Task MigrationThatTimesOutOnALockIsRetried()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await LedgerScope.CreateAsync(postgres.DataSource!);
        await using (var create = scope.DataSource.CreateCommand("create table locked_table (id int)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var blocker = await scope.DataSource.OpenConnectionAsync();
        var blockingTransaction = await blocker.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("select count(*) from locked_table", blocker, blockingTransaction))
        {
            await hold.ExecuteScalarAsync();
        }

        var migration = new AlterLockedTableMigration(() => blockingTransaction.CommitAsync());
        await CreateRunner(scope.DataSource, [migration], FastOptions with { MaxAttempts = 3 }).RunAsync();

        Assert.Equal(2, migration.Attempts);
        await blockingTransaction.DisposeAsync();
    }

    [Fact]
    public async Task MigrationGivesUpAfterTheLastLockTimeout()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await LedgerScope.CreateAsync(postgres.DataSource!);
        await using (var create = scope.DataSource.CreateCommand("create table locked_table (id int)"))
        {
            await create.ExecuteNonQueryAsync();
        }

        await using var blocker = await scope.DataSource.OpenConnectionAsync();
        await using var blockingTransaction = await blocker.BeginTransactionAsync();
        await using (var hold = new NpgsqlCommand("select count(*) from locked_table", blocker, blockingTransaction))
        {
            await hold.ExecuteScalarAsync();
        }

        var migration = new AlterLockedTableMigration(releaseLock: null);
        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            CreateRunner(scope.DataSource, [migration], FastOptions with { MaxAttempts = 2 }).RunAsync());

        Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
        Assert.Equal(2, migration.Attempts);
    }

    [Fact]
    public async Task WaitForAnotherInstanceHoldingTheMigrationLockIsBounded()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var scope = await LedgerScope.CreateAsync(postgres.DataSource!);
        await using var otherInstance = await scope.DataSource.OpenConnectionAsync();
        await using (var hold = new NpgsqlCommand("select pg_advisory_lock(7256394813002)", otherInstance))
        {
            await hold.ExecuteScalarAsync();
        }

        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() =>
                CreateRunner(scope.DataSource, [new EmptyMigration()], FastOptions).RunAsync());

            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                CreateRunner(scope.DataSource, [new EmptyMigration()], FastOptions with
                {
                    AdvisoryLockWait = TimeSpan.FromMinutes(1),
                }).RunAsync(cancelled.Token));
        }
        finally
        {
            await using var release = new NpgsqlCommand("select pg_advisory_unlock(7256394813002)", otherInstance);
            await release.ExecuteScalarAsync();
        }
    }

    private static readonly MigrationRunnerOptions FastOptions = new(
        TimeSpan.FromMilliseconds(200),
        MaxAttempts: 2,
        AdvisoryLockWait: TimeSpan.FromMilliseconds(300))
    {
        RetryDelay = TimeSpan.FromMilliseconds(10),
        AdvisoryLockPollInterval = TimeSpan.FromMilliseconds(50),
    };

    private static MigrationRunner CreateRunner(
        NpgsqlDataSource dataSource,
        IPlaydexMigration[] migrations,
        MigrationRunnerOptions? options = null) =>
        new(dataSource, new MigrationCatalog(migrations), NullLogger<MigrationRunner>.Instance, options);

    /// <summary>Needs ACCESS EXCLUSIVE on a table another session reads; releases that session on its retry.</summary>
    private sealed class AlterLockedTableMigration(Func<Task>? releaseLock) : IPlaydexMigration
    {
        public int Attempts { get; private set; }
        public string Id => "001_alter_locked_table";
        public bool SupportsRollback => false;

        public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts > 1 && releaseLock is not null)
            {
                await releaseLock();
            }

            await using var command = new NpgsqlCommand(
                "alter table locked_table add column name text", connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private sealed class EmptyMigration(string id = "001_test") : IPlaydexMigration
    {
        public string Id => id;
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
