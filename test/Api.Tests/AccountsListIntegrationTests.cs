using Api.Modules.Accounts;
using Api.Modules.Games;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class AccountsListIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task ListsOnlyTheUsersIntegrationsInIdOrderWithTheirKind()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"accounts-list-{suffix}@example.test" };
        var other = new User { Email = $"accounts-list-other-{suffix}@example.test" };
        var platform = new Platform { Key = $"accounts-list-{suffix}", Name = "Accounts list" };
        db.AddRange(user, other, platform);
        await db.SaveChangesAsync();

        var psn = new Integration { UserId = user.Id, Type = IntegrationType.Psn, PlatformId = platform.Id, AccountId = "psn" };
        var api = new Integration { UserId = user.Id, Type = IntegrationType.Api };
        var steam = new Integration { UserId = user.Id, Type = IntegrationType.Steam, PlatformId = platform.Id, AccountId = "steam" };
        var someoneElses = new Integration { UserId = other.Id, Type = IntegrationType.Api };
        db.Integrations.AddRange(psn, api, steam, someoneElses);
        await db.SaveChangesAsync();

        var accounts = await new AccountsRepository(db, new GamesService(new GamesRepository(db)))
            .ListAccounts(user.Id, CancellationToken.None);

        Assert.Equal(
            new[] { psn, api, steam }.OrderBy(integration => integration.Id).Select(integration => integration.Id),
            accounts.Select(account => account.Id));
        Assert.Equal("psn", accounts.Single(account => account.Id == psn.Id).Name);
        Assert.Equal("api", accounts.Single(account => account.Id == api.Id).Name);
        Assert.Equal("steam", accounts.Single(account => account.Id == steam.Id).Name);
    }

    private PlaydexDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);
}
