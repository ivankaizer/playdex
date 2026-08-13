using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;
using Api.Modules.Admin;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class AdminRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CredentialReadsUsePersistedPlatformKeys()
    {
        if (!postgres.IsConfigured) return;

        var options = new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(
                postgres.DataSource!,
                npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                })
            .Options;
        await using var db = new PlaydexDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("N");
        var user = new User { Email = $"admin-credentials-{suffix}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var steamIntegration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Steam,
            PlatformId = steam.Id,
            Active = true,
        };
        var apiIntegration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Api,
            Active = true,
        };
        db.Integrations.AddRange(steamIntegration, apiIntegration);
        await db.SaveChangesAsync();

        var repository = new AdminRepository(db);
        var rows = (await repository.ListCredentials(CancellationToken.None))
            .Where(row => row.UserEmail == user.Email)
            .ToList();
        var steamRow = await repository.GetCredential(
            steamIntegration.Id,
            CancellationToken.None);

        Assert.Equal(["api", "steam"], rows.Select(row => row.Platform));
        Assert.Equal("steam", steamRow?.Platform);
    }
}
