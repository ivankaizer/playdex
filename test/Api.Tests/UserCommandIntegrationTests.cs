using Api.Infrastructure.Auth;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class UserCommandIntegrationTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatesAnAdminWhoseGeneratedPasswordSignsIn()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = $"owner-{Guid.NewGuid():N}@example.com";

        var result = await UserCommand.Upsert(db, email, isAdmin: true, password: null, Now, CancellationToken.None);

        Assert.True(result.Created);
        Assert.True(result.Password.Length >= 20);
        var user = await db.Users.AsNoTracking().SingleAsync(row => row.Email == email);
        Assert.True(user.IsAdmin);
        Assert.Equal(Now, user.CreatedAt);
        Assert.True(BCrypt.Net.BCrypt.Verify(result.Password, user.Password));
    }

    [Fact]
    public async Task ResetsThePasswordOfAnExistingUserAndKeepsAdmin()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = $"owner-{Guid.NewGuid():N}@example.com";
        await UserCommand.Upsert(db, email, isAdmin: true, password: "first", Now, CancellationToken.None);

        var result = await UserCommand.Upsert(db, email, isAdmin: false, password: "second", Now.AddDays(1), CancellationToken.None);

        Assert.False(result.Created);
        var user = await db.Users.AsNoTracking().SingleAsync(row => row.Email == email);
        Assert.True(user.IsAdmin);
        Assert.True(BCrypt.Net.BCrypt.Verify("second", user.Password));
        Assert.Equal(Now, user.CreatedAt);
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
