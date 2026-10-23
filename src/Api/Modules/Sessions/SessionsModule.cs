namespace Api.Modules.Sessions;

public static class SessionsModule
{
    public static IServiceCollection AddSessions(this IServiceCollection services)
    {
        services.AddScoped<SessionLifecycle>();
        return services;
    }
}
