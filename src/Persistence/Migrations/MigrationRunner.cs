using System.Diagnostics;
using System.Globalization;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace Playdex.Persistence.Migrations;

/// <summary>Bounds how long a migration may wait for locks held by other sessions.</summary>
/// <param name="LockTimeout">
/// <c>lock_timeout</c> for every statement of a migration. DDL waiting behind a long-running transaction queues all
/// later traffic on that table, so a migration gives up quickly and retries instead of stalling the application.
/// </param>
/// <param name="MaxAttempts">How many times a migration is tried when it times out waiting for a lock.</param>
/// <param name="AdvisoryLockWait">How long to wait for another instance that is applying migrations.</param>
public sealed record MigrationRunnerOptions(
    TimeSpan LockTimeout,
    int MaxAttempts,
    TimeSpan AdvisoryLockWait)
{
    public static MigrationRunnerOptions Default { get; } = new(
        TimeSpan.FromSeconds(5),
        MaxAttempts: 5,
        AdvisoryLockWait: TimeSpan.FromMinutes(15));

    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan AdvisoryLockPollInterval { get; init; } = TimeSpan.FromSeconds(1);
}

public sealed partial class MigrationRunner(
    NpgsqlDataSource dataSource,
    MigrationCatalog catalog,
    ILogger<MigrationRunner> logger,
    MigrationRunnerOptions? options = null)
{
    private const long AdvisoryLockKey = 7_256_394_813_002;

    private readonly MigrationRunnerOptions _options = options ?? MigrationRunnerOptions.Default;

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying database migration {MigrationId}")]
    private partial void LogApplyingMigration(string migrationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Waiting for another instance to finish applying database migrations")]
    private partial void LogWaitingForMigrationLock();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database migration {MigrationId} timed out waiting for a lock (attempt {Attempt} of {MaxAttempts}); retrying in {Delay}")]
    private partial void LogMigrationLockTimeout(string migrationId, int attempt, int maxAttempts, TimeSpan delay);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await AcquireAdvisoryLock(connection, cancellationToken);

        try
        {
            await EnsureMetadataTable(connection, cancellationToken);
            var applied = await ReadAppliedMigrations(connection, cancellationToken);
            var pending = catalog.GetPendingMigrations(applied);

            foreach (var migration in pending)
            {
                await ApplyWithRetry(connection, migration, cancellationToken);
            }

            await connection.ReloadTypesAsync(cancellationToken);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("select pg_advisory_unlock(@key)", connection);
            unlock.Parameters.AddWithValue("key", AdvisoryLockKey);
            await unlock.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Polls <c>pg_try_advisory_lock</c> rather than blocking in <c>pg_advisory_lock</c>, so a hung migration on
    /// another instance cannot stall this one forever and shutdown can interrupt the wait.
    /// </summary>
    private async Task AcquireAdvisoryLock(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        var waited = Stopwatch.StartNew();
        await using var command = new NpgsqlCommand("select pg_try_advisory_lock(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        for (var attempt = 0; await command.ExecuteScalarAsync(cancellationToken) is not true; attempt++)
        {
            if (waited.Elapsed >= _options.AdvisoryLockWait)
            {
                throw new TimeoutException(
                    $"Another instance has held the database migration lock for over {_options.AdvisoryLockWait}. " +
                    "Check pg_stat_activity for a stuck migration, then restart.");
            }

            if (attempt == 0)
            {
                LogWaitingForMigrationLock();
            }

            await Task.Delay(_options.AdvisoryLockPollInterval, cancellationToken);
        }
    }

    private async Task ApplyWithRetry(
        NpgsqlConnection connection,
        IPlaydexMigration migration,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ApplyMigration(connection, migration, cancellationToken);
                return;
            }
            catch (PostgresException exception) when (
                exception.SqlState == PostgresErrorCodes.LockNotAvailable && attempt < _options.MaxAttempts)
            {
                var delay = _options.RetryDelay * attempt;
                LogMigrationLockTimeout(migration.Id, attempt, _options.MaxAttempts, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static async Task EnsureMetadataTable(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        const string sql = """
            create table if not exists dotnet_schema_migrations (
                migration_id text primary key,
                applied_at timestamptz not null default now(),
                supports_rollback boolean not null
            )
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);

        // Databases adopted from the legacy migration ledger may have the
        // columns but not the defaults declared above. Avoid ALTER TABLE when
        // a default already exists: even an unchanged default requires an
        // exclusive lock and can block startup behind a reading transaction.
        await using var checkDefault = new NpgsqlCommand(
            """
            select atthasdef
            from pg_catalog.pg_attribute
            where attrelid = 'dotnet_schema_migrations'::regclass
              and attname = 'applied_at'
              and not attisdropped
            """,
            connection);
        if (await checkDefault.ExecuteScalarAsync(cancellationToken) is true)
        {
            return;
        }

        await using var defaults = new NpgsqlCommand(
            "alter table dotnet_schema_migrations alter column applied_at set default now()",
            connection);
        await defaults.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> ReadAppliedMigrations(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        // Ordinal order, as MigrationCatalog compares ids; the database collation may sort '_' differently.
        const string sql = """
            select migration_id from dotnet_schema_migrations order by migration_id collate "C"
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            applied.Add(reader.GetString(0));
        }

        return applied;
    }

    private async Task ApplyMigration(
        NpgsqlConnection connection,
        IPlaydexMigration migration,
        CancellationToken cancellationToken)
    {
        LogApplyingMigration(migration.Id);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var lockTimeout = new NpgsqlCommand(
                "select set_config('lock_timeout', @timeout, true)", connection, transaction))
            {
                lockTimeout.Parameters.AddWithValue(
                    "timeout",
                    ((long)_options.LockTimeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + "ms");
                await lockTimeout.ExecuteScalarAsync(cancellationToken);
            }

            await migration.ApplyAsync(connection, transaction, cancellationToken);
            const string sql = """
                insert into dotnet_schema_migrations (migration_id, supports_rollback)
                values (@migrationId, @supportsRollback)
                """;
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("migrationId", migration.Id);
            command.Parameters.AddWithValue("supportsRollback", migration.SupportsRollback);
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
