using Api.Infrastructure.Common;

namespace Api.Modules.Playnite;

public static class PlayniteModule
{
    public static IServiceCollection AddPlaynite(this IServiceCollection services)
    {
        services.AddScoped<PlayniteRepository>();
        services.AddScoped<PlayniteService>();
        return services;
    }

    public static RouteGroupBuilder MapPlaynite(this RouteGroupBuilder api)
    {
        PlayniteEndpoints.MapPlayniteEndpoints(api);
        return api;
    }
}
