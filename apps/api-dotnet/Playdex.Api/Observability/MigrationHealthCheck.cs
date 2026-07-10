using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Playdex.Api.Data.Migrations;

namespace Playdex.Api.Observability;

public sealed class MigrationHealthCheck(
    NpgsqlDataSource dataSource,
    MigrationCatalog catalog,
    ILogger<MigrationHealthCheck> logger) : IHealthCheck
{
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

            catalog.ValidateApplied(applied);

            if (applied.Count < catalog.Migrations.Count)
            {
                var pending = catalog.Migrations
                    .Skip(applied.Count)
                    .Select(m => m.Id);

                return HealthCheckResult.Degraded($"Pending migrations: {string.Join(", ", pending)}");
            }

            return HealthCheckResult.Healthy("All migrations applied.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Migration health check failed");
            return HealthCheckResult.Unhealthy("Migration check failed.", ex);
        }
    }
}
