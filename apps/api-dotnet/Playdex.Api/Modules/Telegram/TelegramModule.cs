namespace Playdex.Api.Modules.Telegram;

public static class TelegramModule
{
    public static IServiceCollection AddTelegram(this IServiceCollection services)
    {
        services.AddScoped<TelegramRepository>();
        services.AddHostedService<TelegramBackgroundService>();
        return services;
    }
}
