using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Modules.Privacy;

public static class PrivacyModule
{
    public static IServiceCollection AddPrivacy(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<PrivacyRepository>();
        services.AddScoped<PrivacyService>();
        return services;
    }

    public static RouteGroupBuilder MapPrivacy(this RouteGroupBuilder api)
    {
        PrivacyEndpoints.MapPrivacyEndpoints(api);
        return api;
    }
}
