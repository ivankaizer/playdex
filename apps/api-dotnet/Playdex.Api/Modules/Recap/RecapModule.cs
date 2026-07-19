namespace Playdex.Api.Modules.Recap;

public static class RecapModule
{
    public static IServiceCollection AddRecap(this IServiceCollection services)
    {
        services.AddScoped<RecapRepository>();
        services.AddScoped<RecapService>();
        return services;
    }

    public static RouteGroupBuilder MapRecap(this RouteGroupBuilder api)
    {
        RecapEndpoints.MapRecapEndpoints(api);
        return api;
    }
}
