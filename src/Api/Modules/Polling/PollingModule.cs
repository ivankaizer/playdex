using Api.Modules.Playnite;

namespace Api.Modules.Polling;

public static class PollingModule
{
    public static IServiceCollection AddPolling(this IServiceCollection services)
    {
        services.AddScoped<PollingRepository>();
        services.AddHttpClient<IProviderClient, SteamClient>("SteamClient")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IProviderClient, PsnClient>("PsnClient")
            .ConfigureHttpClient(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IgdbClient>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddHostedService<PollingBackgroundService>();
        return services;
    }
}
