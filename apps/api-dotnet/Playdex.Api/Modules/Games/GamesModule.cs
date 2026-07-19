namespace Playdex.Api.Modules.Games;

public static class GamesModule
{
    public static IServiceCollection AddGames(this IServiceCollection services)
    {
        services.AddScoped<GamesRepository>();
        services.AddScoped<GamesService>();
        services.AddScoped<IGames>(sp => sp.GetRequiredService<GamesService>());
        return services;
    }

    public static RouteGroupBuilder MapGames(this RouteGroupBuilder api)
    {
        GamesEndpoints.MapGamesEndpoints(api);
        return api;
    }
}
