using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Api.Observability;

public static class HealthEndpoints
{
    /// <summary>
    /// Ready only when everything is healthy. Degraded (for example pending migrations) used to answer 200, so a load
    /// balancer sent traffic to an instance whose schema was behind its code.
    /// </summary>
    public static HealthCheckOptions Readiness { get; } = new()
    {
        ResultStatusCodes =
        {
            [HealthStatus.Healthy] = StatusCodes.Status200OK,
            [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
            [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
        },
    };
}
