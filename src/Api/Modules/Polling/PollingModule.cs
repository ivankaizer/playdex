namespace Api.Modules.Polling;

public static class PollingModule
{
    public static IServiceCollection AddPolling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PollingOptions>(configuration.GetSection("Polling"));
        services.Configure<SteamOptions>(configuration.GetSection("Polling:Steam"));
        services.Configure<PsnOptions>(configuration.GetSection("Polling:Psn"));
        services.Configure<IgdbOptions>(configuration.GetSection("Polling:Igdb"));

        services.AddScoped<PollingRepository>();
        services.AddHttpClient<IProviderClient, SteamClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IProviderClient, PsnClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IgdbClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHostedService<PollingBackgroundService>();
        return services;
    }
}
