using Npgsql;

namespace Api.Shared.Persistence.Migrations;

public sealed class MigrationRunner(
    NpgsqlDataSource dataSource,
    MigrationCatalog catalog,
    ILogger<MigrationRunner> logger)
{
    private const long AdvisoryLockKey = 7_256_394_813_002;

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await SetAdvisoryLock(connection, "pg_advisory_lock", cancellationToken);

        try
        {
            await EnsureMetadataTable(connection, cancellationToken);
            var applied = await ReadAppliedMigrations(connection, cancellationToken);
            catalog.ValidateApplied(applied);

            foreach (var migration in catalog.Migrations.Skip(applied.Count))
            {
                await ApplyMigration(connection, migration, cancellationToken);
            }
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
        logger.LogInformation("Applying database migration {MigrationId}", migration.Id);
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
