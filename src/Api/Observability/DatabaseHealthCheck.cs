using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Api.Observability;

public sealed partial class DatabaseHealthCheck(
    NpgsqlDataSource dataSource,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    [LoggerMessage(Level = LogLevel.Error, Message = "Database health check failed")]
    private partial void LogDatabaseHealthCheckFailed(Exception exception);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy("Database connectivity verified.");
        }
        catch (Exception ex)
        {
            LogDatabaseHealthCheckFailed(ex);
            return HealthCheckResult.Unhealthy("Database unreachable.", ex);
        }
    }
}
