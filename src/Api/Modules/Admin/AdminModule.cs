namespace Api.Modules.Admin;

public static class AdminModule
{
    public static IServiceCollection AddAdmin(this IServiceCollection services)
    {
        services.AddScoped<AdminRepository>();
        services.AddScoped<AdminService>();
        return services;
    }

    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder api)
    {
        AdminEndpoints.MapAdminEndpoints(api);
        return api;
    }
}
