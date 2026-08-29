using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;
using Shared.Persistence.Migrations;

namespace Api.Observability;

public sealed partial class MigrationHealthCheck(
    NpgsqlDataSource dataSource,
    MigrationCatalog catalog,
    ILogger<MigrationHealthCheck> logger) : IHealthCheck
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Migration health check failed")]
    private partial void LogMigrationHealthCheckFailed(Exception exception);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);

            const string sql = "SELECT migration_id FROM dotnet_schema_migrations ORDER BY migration_id";
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var applied = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }

            var pending = catalog.GetPendingMigrations(applied);

            if (pending.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"Pending migrations: {string.Join(", ", pending.Select(migration => migration.Id))}");
            }

            return HealthCheckResult.Healthy("All migrations applied.");
        }
        catch (Exception ex)
        {
            LogMigrationHealthCheckFailed(ex);
            return HealthCheckResult.Unhealthy("Migration check failed.", ex);
        }
    }
}
