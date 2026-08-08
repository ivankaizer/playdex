using Microsoft.AspNetCore.Authentication.JwtBearer;
using Api.Shared.Persistence;

namespace Api.Shared.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection("Auth"));
        services.AddScoped<AuthRepository>();
        services.AddScoped<AuthService>();
        services.AddPlaydexAuthentication();
        return services;
    }

    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        AuthEndpoints.MapAuthEndpoints(api);
        return api;
    }
}
