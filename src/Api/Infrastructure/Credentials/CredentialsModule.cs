namespace Api.Infrastructure.Credentials;

public static class CredentialsModule
{
    public static IServiceCollection AddCredentialEncryption(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CredentialOptions>()
            .Bind(configuration.GetSection("Credentials"))
            .Validate(
                options => CredentialOptions.IsLongEnough(options.EncryptionKey),
                $"Credentials:EncryptionKey (CREDENTIALS_KEY) must be at least {CredentialOptions.MinimumKeyBytes} bytes. Generate one with: openssl rand -hex 32")
            .Validate<IHostEnvironment>(
                (options, environment) => environment.IsDevelopment()
                    || !CredentialOptions.PublicDevelopmentKeys.Contains(options.EncryptionKey),
                "Credentials:EncryptionKey (CREDENTIALS_KEY) is a development value published in the repository. Generate one with: openssl rand -hex 32")
            .ValidateOnStart();
        services.AddSingleton<CredentialProtector>();
        services.AddHostedService<CredentialEncryptionBackfill>();
        return services;
    }
}
