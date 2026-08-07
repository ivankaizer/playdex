namespace Playdex.Api.Modules.Accounts;

public static class AccountsModule
{
    public static IServiceCollection AddAccounts(this IServiceCollection services)
    {
        services.AddScoped<AccountsRepository>();
        services.AddScoped<AccountsService>();
        return services;
    }

    public static RouteGroupBuilder MapAccounts(this RouteGroupBuilder api)
    {
        AccountsEndpoints.MapAccountsEndpoints(api);
        return api;
    }
}
