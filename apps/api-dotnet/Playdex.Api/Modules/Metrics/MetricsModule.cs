namespace Playdex.Api.Modules.Metrics;

public static class MetricsModule
{
    public static IServiceCollection AddPlaydexMetrics(this IServiceCollection services)
    {
        services.AddSingleton<MetricsCollector>();
        return services;
    }

    public static RouteGroupBuilder MapMetrics(this RouteGroupBuilder api)
    {
        MetricsEndpoints.MapMetricsEndpoints(api);
        return api;
    }
}
