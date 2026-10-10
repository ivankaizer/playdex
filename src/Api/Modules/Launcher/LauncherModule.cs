namespace Api.Modules.Launcher;

/// <summary>
/// Read access for the Windows launcher, which holds only the user's API key: <c>GET /api/launcher/summary</c>.
/// </summary>
public static class LauncherModule
{
    public static IServiceCollection AddLauncher(this IServiceCollection services)
    {
        services.AddScoped<LauncherRepository>();
        return services;
    }

    public static RouteGroupBuilder MapLauncher(this RouteGroupBuilder api)
    {
        LauncherEndpoints.MapLauncherEndpoints(api);
        return api;
    }
}
