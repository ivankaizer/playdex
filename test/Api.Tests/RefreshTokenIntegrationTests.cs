using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class RefreshTokenIntegrationTests(PostgresFixture postgres)
{
    private static readonly AuthTokenService Tokens =
        new(Options.Create(new AuthOptions { JwtSecret = "refresh-token-integration-secret-at-least-32-bytes" }));

    [Fact]
    public async Task EachRefreshTokenWorksOnceAndReusingOneRevokesTheSignIn()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = await CreateUser(db);
        var service = Service(db);

        var login = await service.Login(email, "secret");
        var refreshed = await service.Refresh(login.RefreshToken);

        var stored = await StoredTokens(db, login.UserId);
        Assert.Equal(2, stored.Count);
        Assert.Single(stored.Select(row => row.FamilyId).Distinct());
        Assert.NotNull(stored[0].RotatedAt);
        Assert.Null(stored[1].RotatedAt);
        var refreshedId = Tokens.Validate(refreshed.RefreshToken, "refresh").FindFirst(AuthTokenService.TokenIdClaim)!.Value;
        Assert.Equal(AuthTokenService.HashTokenId(refreshedId), stored[1].TokenHash);

        var reuse = await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(login.RefreshToken));
        var successor = await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(refreshed.RefreshToken));

        Assert.Equal(401, reuse.StatusCode);
        Assert.Equal(401, successor.StatusCode);
        Assert.Empty(await StoredTokens(db, login.UserId));
    }

    [Fact]
    public async Task ATokenCanBeRotatedOnlyOnce()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = await CreateUser(db);
        var login = await Service(db).Login(email, "secret");
        var current = (await StoredTokens(db, login.UserId)).Single();
        var repository = new AuthRepository(db);

        Assert.True(await repository.RotateRefreshToken(current.Id, Successor(current)));
        Assert.False(await repository.RotateRefreshToken(current.Id, Successor(current)));
        Assert.Equal(2, (await StoredTokens(db, login.UserId)).Count);
    }

    [Fact]
    public async Task LogoutAndANewPasswordRevokeStoredTokens()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var email = await CreateUser(db);
        var service = Service(db);
        var signedOut = await service.Login(email, "secret");
        var otherDevice = await service.Login(email, "secret");

        await service.Logout(signedOut.RefreshToken);

        await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(signedOut.RefreshToken));
        Assert.Single(await StoredTokens(db, otherDevice.UserId));

        await UserCommand.Upsert(db, email, isAdmin: false, password: "changed", DateTimeOffset.UtcNow, CancellationToken.None);

        await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(otherDevice.RefreshToken));
        Assert.Empty(await StoredTokens(db, otherDevice.UserId));
    }

    private static RefreshToken Successor(RefreshToken current) => new()
    {
        UserId = current.UserId,
        FamilyId = current.FamilyId,
        TokenHash = AuthTokenService.HashTokenId(Guid.NewGuid().ToString("N")),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
    };

    private static async Task<string> CreateUser(PlaydexDbContext db)
    {
        var email = $"refresh-{Guid.NewGuid():N}@example.com";
        await UserCommand.Upsert(db, email, isAdmin: false, password: "secret", DateTimeOffset.UtcNow, CancellationToken.None);
        return email;
    }

    private static Task<List<RefreshToken>> StoredTokens(PlaydexDbContext db, long userId) =>
        db.RefreshTokens.AsNoTracking().Where(row => row.UserId == userId).OrderBy(row => row.Id).ToListAsync();

    private static AuthService Service(PlaydexDbContext db) =>
        new(new AuthRepository(db), Tokens, NullLogger<AuthService>.Instance);

    private PlaydexDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);
}
