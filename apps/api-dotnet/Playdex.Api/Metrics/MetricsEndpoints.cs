using System.Globalization;
using System.Security.Claims;
using Playdex.Api.Http;

namespace Playdex.Api.Metrics;

public static class MetricsEndpoints
{
    public static void MapMetricsEndpoints(this RouteGroupBuilder api)
    {
        var metrics = api.MapGroup("/metrics").RequireAuthorization();
        metrics.MapGet("", GetMetrics);
    }

    private static async Task<ApiResponse<MetricsSnapshotDto>> GetMetrics(
        ClaimsPrincipal principal,
        MetricsCollector collector,
        CancellationToken ct)
    {
        var userId = GetUserId(principal);
        return ApiResponse<MetricsSnapshotDto>.Ok(collector.Snapshot());
    }

    private static long GetUserId(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue("sub");
        if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
        {
            throw AppProblem.Unauthorized("Invalid token.");
        }
        return userId;
    }
}
