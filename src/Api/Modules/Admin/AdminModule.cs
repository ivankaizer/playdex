using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Modules.Admin;

public static class AdminModule
{
    public static IServiceCollection AddAdmin(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<AdminRepository>();
        services.AddScoped<AdminService>();
        services.AddScoped<IAuthorizationHandler, AdministratorRequirementHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(AdministratorRequirement.PolicyName, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AdministratorRequirement()));
        return services;
    }

    public static RouteGroupBuilder MapAdmin(this RouteGroupBuilder api)
    {
        AdminEndpoints.MapAdminEndpoints(api);
        return api;
    }
}
