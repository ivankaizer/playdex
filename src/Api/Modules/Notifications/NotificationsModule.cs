using Api.Infrastructure.Common;

namespace Api.Modules.Notifications;

public static class NotificationsModule
{
    public static IServiceCollection AddNotifications(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration.GetSection("Apprise").Get<AppriseOptions>() ?? new();
        services.Configure<AppriseOptions>(configuration.GetSection("Apprise"));
        services.AddScoped<NotificationsRepository>();
        services.AddHttpClient<AppriseClient>(client =>
        {
            if (!string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                var baseUrl = options.BaseUrl.TrimEnd('/') + "/";
                client.BaseAddress = new Uri(baseUrl, UriKind.Absolute);
            }
            client.Timeout = TimeSpan.FromSeconds(60);
        });
        services.AddHttpClient<NotificationRenderer>(client =>
            client.Timeout = TimeSpan.FromSeconds(15));
        services.AddScoped<NotificationEventHandlers>();
        services.AddScoped<IEventSubscriber<SessionStarted>>(
            provider => provider.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IEventSubscriber<SessionEnded>>(
            provider => provider.GetRequiredService<NotificationEventHandlers>());
        services.AddScoped<IEventSubscriber<PollFailed>>(
            provider => provider.GetRequiredService<NotificationEventHandlers>());
        return services;
    }
}
