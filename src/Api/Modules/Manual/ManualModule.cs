using Api.Shared.Common;

namespace Api.Modules.Manual;

public static class ManualModule
{
    public static IServiceCollection AddManual(this IServiceCollection services)
    {
        services.AddScoped<ManualRepository>();
        services.AddScoped<ManualService>();
        services.AddScoped<IEventHandler<StartSessionRequested>, ManualSessionHandler>();
        services.AddScoped<IEventHandler<StopSessionRequested>, ManualStopHandler>();
        return services;
    }

    public static RouteGroupBuilder MapManual(this RouteGroupBuilder api)
    {
        ManualEndpoints.MapManualEndpoints(api);
        return api;
    }
}
