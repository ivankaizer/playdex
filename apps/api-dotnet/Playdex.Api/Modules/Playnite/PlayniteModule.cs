using Playdex.Api.Shared.Common;

namespace Playdex.Api.Modules.Playnite;

public static class PlayniteModule
{
    public static IServiceCollection AddPlaynite(this IServiceCollection services)
    {
        services.AddScoped<PlayniteRepository>();
        services.AddScoped<PlayniteService>();
        services.AddScoped<IEventHandler<GameDetected>, GameDetectedHandler>();
        return services;
    }

    public static RouteGroupBuilder MapPlaynite(this RouteGroupBuilder api)
    {
        PlayniteEndpoints.MapPlayniteEndpoints(api);
        return api;
    }
}
