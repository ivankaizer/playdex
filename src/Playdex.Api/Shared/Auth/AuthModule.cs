using Microsoft.AspNetCore.Authentication.JwtBearer;
using Playdex.Api.Shared.Common;
using Playdex.Api.Shared.Persistence;

namespace Playdex.Api.Shared.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuth(this IServiceCollection services, AuthTokenService tokenService)
    {
        services.AddScoped<AuthRepository>();
        services.AddScoped<AuthService>();
        services.AddPlaydexAuthentication(tokenService);
        return services;
    }

    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        AuthEndpoints.MapAuthEndpoints(api);
        return api;
    }
}
