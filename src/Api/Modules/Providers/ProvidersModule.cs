namespace Api.Modules.Providers;

public static class ProvidersModule
{
    public static IServiceCollection AddProviders(this IServiceCollection services)
    {
        services.AddScoped<ProvidersRepository>();
        services.AddScoped<ProvidersService>();
        return services;
    }

    public static RouteGroupBuilder MapProviders(this RouteGroupBuilder api)
    {
        ProvidersEndpoints.MapProvidersEndpoints(api);
        return api;
    }
}
