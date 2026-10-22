using Microsoft.AspNetCore.Authentication.JwtBearer;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Auth"))
            .Validate(
                options => AuthOptions.IsLongEnough(options.JwtSecret),
                $"Auth:JwtSecret (JWT_SECRET) must be at least {AuthOptions.MinimumSecretBytes} bytes. Generate one with: openssl rand -hex 32")
            .Validate<IHostEnvironment>(
                (options, environment) => environment.IsDevelopment()
                    || !AuthOptions.PublicDevelopmentSecrets.Contains(options.JwtSecret),
                "Auth:JwtSecret (JWT_SECRET) is a development value published in the repository. Generate one with: openssl rand -hex 32")
            .ValidateOnStart();
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
