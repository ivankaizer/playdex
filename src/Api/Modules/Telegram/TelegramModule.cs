namespace Api.Modules.Telegram;

public static class TelegramModule
{
    public static IServiceCollection AddTelegram(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TelegramOptions>(configuration.GetSection("Telegram"));

        services.AddScoped<TelegramRepository>();
        services.AddHttpClient<TelegramClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.telegram.org/");
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<TelegramNotificationRenderer>(client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<TelegramEventHandlers>();
        services.AddScoped<Api.Shared.Common.IEventHandler<Api.Shared.Common.SessionStarted>>(
            services => services.GetRequiredService<TelegramEventHandlers>());
        services.AddScoped<Api.Shared.Common.IEventHandler<Api.Shared.Common.SessionEnded>>(
            services => services.GetRequiredService<TelegramEventHandlers>());
        services.AddScoped<Api.Shared.Common.IEventHandler<Api.Shared.Common.PollFailed>>(
            services => services.GetRequiredService<TelegramEventHandlers>());
        services.AddHostedService<TelegramBackgroundService>();
        return services;
    }
}
