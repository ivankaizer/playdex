using Npgsql;
using Microsoft.Extensions.Logging;

namespace Playdex.Persistence.Migrations;

public sealed partial class MigrationRunner(
    NpgsqlDataSource dataSource,
    MigrationCatalog catalog,
    ILogger<MigrationRunner> logger)
{
    private const long AdvisoryLockKey = 7_256_394_813_002;

    [LoggerMessage(Level = LogLevel.Information, Message = "Applying database migration {MigrationId}")]
    private partial void LogApplyingMigration(string migrationId);

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await SetAdvisoryLock(connection, "pg_advisory_lock", cancellationToken);

        try
        {
            await EnsureMetadataTable(connection, cancellationToken);
            var applied = await ReadAppliedMigrations(connection, cancellationToken);
            var pending = catalog.GetPendingMigrations(applied);

            foreach (var migration in pending)
            {
                await ApplyMigration(connection, migration, cancellationToken);
            }

            await connection.ReloadTypesAsync(cancellationToken);
        }
        finally
        {
            await SetAdvisoryLock(connection, "pg_advisory_unlock", CancellationToken.None);
        }
    }

    private static async Task SetAdvisoryLock(
        NpgsqlConnection connection,
        string function,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"select {function}(@key)", connection);
        command.Parameters.AddWithValue("key", AdvisoryLockKey);
        await command.ExecuteScalarAsync(cancellationToken);
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
        const string sql = "select migration_id from dotnet_schema_migrations order by migration_id";
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
