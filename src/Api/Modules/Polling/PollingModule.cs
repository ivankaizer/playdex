using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenTelemetry.Trace;

namespace Api.Modules.Polling;

public static class PollingModule
{
    public static IServiceCollection AddPolling(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PollingOptions>(configuration.GetSection("Polling"));
        services.Configure<SteamOptions>(configuration.GetSection("Polling:Steam"));
        services.Configure<PsnOptions>(configuration.GetSection("Polling:Psn"));
        services.Configure<OpenXblOptions>(configuration.GetSection("Polling:OpenXbl"));
        services.Configure<IgdbOptions>(configuration.GetSection("Polling:Igdb"));

        services.AddScoped<PollingRepository>();
        services.AddSingleton<PsnTokenCache>();
        services.AddHttpClient<IProviderClient, SteamClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IProviderClient, PsnClient>(c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
            });
        services.AddHttpClient<IProviderClient, OpenXblClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddHttpClient<IgdbClient>(c => c.Timeout = TimeSpan.FromSeconds(15));
        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddSource(PollingBackgroundService.ActivitySourceName));
        services.AddSingleton<PollingLease>();
        services.AddSingleton<CapsuleLookupBackoff>();
        services.AddHostedService<PollingBackgroundService>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<PollResultRetention>();
        services.AddHostedService<PollResultCleanup>();
        return services;
    }
}
