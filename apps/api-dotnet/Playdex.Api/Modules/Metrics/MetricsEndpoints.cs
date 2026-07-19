using System.Security.Claims;
using Playdex.Api.Shared.Auth;

namespace Playdex.Api.Modules.Metrics;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this RouteGroupBuilder api)
    {
        var metrics = api.MapGroup("/metrics").RequireAuthorization();
        metrics.MapGet("", GetMetrics);
    }

    private static Task<IResult> GetMetrics(
        ClaimsPrincipal principal,
        MetricsCollector collector,
        CancellationToken ct)
    {
        return Task.FromResult<IResult>(TypedResults.Ok(collector.Snapshot()));
    }
}
