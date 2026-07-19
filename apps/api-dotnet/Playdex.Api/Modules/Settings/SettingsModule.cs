namespace Playdex.Api.Modules.Settings;

public static class SettingsModule
{
    public static IServiceCollection AddSettings(this IServiceCollection services)
    {
        services.AddScoped<SettingsRepository>();
        services.AddScoped<SettingsService>();
        return services;
    }

    public static RouteGroupBuilder MapSettings(this RouteGroupBuilder api)
    {
        SettingsEndpoints.MapSettingsEndpoints(api);
        return api;
    }
}
