using Api.Infrastructure.Common;

namespace Api.Modules.ApiCapture;

public static class ApiCaptureModule
{
    public static IServiceCollection AddApiCapture(this IServiceCollection services)
    {
        services.AddScoped<ApiCaptureRepository>();
        services.AddScoped<ApiCaptureService>();
        services.AddScoped<IApiSessionTimeoutCleaner>(services =>
            services.GetRequiredService<ApiCaptureService>());
        return services;
    }

    public static RouteGroupBuilder MapApiCapture(this RouteGroupBuilder api)
    {
        ApiCaptureEndpoints.MapApiCaptureEndpoints(api);
        return api;
    }
}
