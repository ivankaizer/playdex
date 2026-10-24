using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;

namespace Api.Infrastructure.Credentials;

/// <summary>Encrypts the integration credentials stored before encryption at rest existed, once, at startup.</summary>
public sealed partial class CredentialEncryptionBackfill(
    IServiceScopeFactory scopeFactory,
    CredentialProtector protector,
    ILogger<CredentialEncryptionBackfill> logger) : IHostedService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Encrypted {Count} plaintext integration credentials")]
    private partial void LogBackfilled(int count);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlaydexDbContext>();
        var count = await Run(db, protector, cancellationToken);
        if (count > 0)
        {
            LogBackfilled(count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <returns>How many credentials it encrypted.</returns>
    public static async Task<int> Run(PlaydexDbContext db, CredentialProtector protector, CancellationToken ct)
    {
        var plaintext = await db.Integrations
            .AsNoTracking()
            .Where(integration => integration.Type != IntegrationType.Api
                && integration.ApiKey != null
                && !integration.ApiKey.StartsWith(CredentialProtector.Prefix))
            .Select(integration => new { integration.Id, integration.ApiKey })
            .ToListAsync(ct);
        var encrypted = 0;
        foreach (var row in plaintext)
        {
            // Only while the row still holds what was read: another instance may have encrypted it, or the user
            // saved a new key, in the meantime.
            var encryptedKey = protector.Protect(row.ApiKey);
            encrypted += await db.Integrations
                .Where(integration => integration.Id == row.Id && integration.ApiKey == row.ApiKey)
                .ExecuteUpdateAsync(updates => updates.SetProperty(integration => integration.ApiKey, encryptedKey), ct);
        }

        return encrypted;
    }
}
