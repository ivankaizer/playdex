using OpenTelemetry.Metrics;

namespace Api.Modules.Sessions;

public static class SessionsModule
{
    public static IServiceCollection AddSessions(this IServiceCollection services)
    {
        services.AddScoped<SessionLifecycle>();
        services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(SessionMetrics.MeterName));
        return services;
    }
}
