using Api.Modules.Admin;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api.Modules.Metrics;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this RouteGroupBuilder api)
    {
        // Instance-wide polling statistics: for administrators, not for every signed-in user.
        var metrics = api.MapGroup("/metrics").RequireAuthorization(AdministratorRequirement.PolicyName);
        metrics.MapGet("", GetMetrics);
    }

    private static Ok<MetricsSnapshotDto> GetMetrics(MetricsCollector collector) => TypedResults.Ok(collector.Snapshot());
}
