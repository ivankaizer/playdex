using Shared.Persistence;
using Api.Modules.Providers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ProvidersRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CreateProviderAllowsThreeOfATypeAndRejectsTheFourth()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Email = $"provider-creation-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        try
        {
            var repository = new ProvidersRepository(db);
            var steam1 = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-1", "steam-id-1", default);
            var steam2 = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-2", "steam-id-2", default);
            var steam3 = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-3", "steam-id-3", default);
            var psn = await repository.CreateProvider(
                user.Id, "psn", IntegrationType.Psn, "npsso-token", null, default);
            var xbox = await repository.CreateProvider(
                user.Id, "xbox", IntegrationType.Xbox, "openxbl-key", "2533274817903789", default);
            var fourthSteam = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-4", "steam-id-4", default);

            Assert.Equal("active", steam1.Provider?.Status);
            Assert.Equal("active", steam2.Provider?.Status);
            Assert.Equal("active", steam3.Provider?.Status);
            Assert.Equal("active", psn.Provider?.Status);
            Assert.Equal("active", xbox.Provider?.Status);
            Assert.False(steam1.LimitReached);
            Assert.False(steam2.LimitReached);
            Assert.False(steam3.LimitReached);
            Assert.False(psn.LimitReached);
            Assert.False(xbox.LimitReached);
            Assert.True(fourthSteam.LimitReached);
            Assert.Null(fourthSteam.Provider);

            var persisted = await db.Integrations
                .AsNoTracking()
                .Where(integration => integration.UserId == user.Id)
                .OrderBy(integration => integration.Type)
                .ToListAsync();
            Assert.Equal(5, persisted.Count);
            Assert.All(persisted, integration => Assert.True(integration.Active));
            Assert.Equal(3, persisted.Count(item => item.Type == IntegrationType.Steam));
            Assert.Equal(
                ["steam-key-1", "steam-key-2", "steam-key-3"],
                persisted.Where(item => item.Type == IntegrationType.Steam)
                    .OrderBy(item => item.AccountId)
                    .Select(item => item.ApiKey));
            Assert.Equal("npsso-token", persisted.Single(item => item.Type == IntegrationType.Psn).ApiKey);
            Assert.Equal("openxbl-key", persisted.Single(item => item.Type == IntegrationType.Xbox).ApiKey);
        }
        finally
        {
            db.Integrations.RemoveRange(db.Integrations.Where(item => item.UserId == user.Id));
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(
                postgres.DataSource!,
                npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                })
            .Options);
}
