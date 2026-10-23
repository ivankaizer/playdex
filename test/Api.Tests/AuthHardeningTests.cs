using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class AuthHardeningTests
{
    private const string StrongSecret = "auth-hardening-test-secret-at-least-thirty-two-bytes";
    private static readonly AuthTokenService _tokens = new(Options.Create(new AuthOptions { JwtSecret = StrongSecret }));

    [Theory]
    [InlineData("")]
    [InlineData("dark-secret")]
    [InlineData("thirty-one-bytes-is-not-enough!")]
    public void ShortJwtSecretsAreRejected(string secret)
    {
        var options = AuthOptionsFor(secret, Environments.Production);

        var error = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains("at least 32 bytes", error.Message);
    }

    [Fact]
    public void PublishedDevelopmentSecretIsRejectedOutsideDevelopment()
    {
        var options = AuthOptionsFor("dev-only-jwt-secret-do-not-use-in-production", Environments.Production);

        var error = Assert.Throws<OptionsValidationException>(() => options.Value);
        Assert.Contains("development value", error.Message);
    }

    [Fact]
    public void PublishedDevelopmentSecretWorksInDevelopment()
    {
        var options = AuthOptionsFor("dev-only-jwt-secret-do-not-use-in-production", Environments.Development);

        Assert.Equal("dev-only-jwt-secret-do-not-use-in-production", options.Value.JwtSecret);
    }

    [Fact]
    public async Task RefreshTokensFromAnOlderTokenVersionAreRevoked()
    {
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "a@example.com", TokenVersion = 1 });
        var oldPair = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);
        repository.Remember(oldPair);

        var error = await Assert.ThrowsAsync<AppProblemException>(() => Service(repository).Refresh(oldPair.RefreshToken));

        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task RefreshReissuesWithTheCurrentEmailAndVersion()
    {
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "new@example.com", TokenVersion = 2 });
        var pair = _tokens.IssueTokenPair(7, "old@example.com", tokenVersion: 2);
        repository.Remember(pair);

        var refreshed = await Service(repository).Refresh(pair.RefreshToken);

        var principal = _tokens.Validate(refreshed.AccessToken, "access");
        Assert.Equal("new@example.com", principal.FindFirst("email")?.Value);
        Assert.Equal("2", principal.FindFirst(AuthTokenService.TokenVersionClaim)?.Value);
    }

    [Fact]
    public async Task ARefreshTokenTheServerNeverStoredIsRejected()
    {
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "a@example.com" });
        var unknown = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);

        var error = await Assert.ThrowsAsync<AppProblemException>(() => Service(repository).Refresh(unknown.RefreshToken));

        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task ARefreshTokenWorksOnceAndReusingItRevokesTheWholeSignIn()
    {
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "a@example.com" });
        var service = Service(repository);
        var first = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);
        repository.Remember(first);
        var second = await service.Refresh(first.RefreshToken);

        var reuse = await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(first.RefreshToken));
        var successor = await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(second.RefreshToken));

        Assert.Equal(401, reuse.StatusCode);
        Assert.Equal(401, successor.StatusCode);
        Assert.Empty(repository.Stored);
    }

    [Fact]
    public async Task ReusingATokenLeavesTheUsersOtherSignInsAlone()
    {
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "a@example.com" });
        var service = Service(repository);
        var laptop = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);
        var phone = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);
        repository.Remember(laptop);
        repository.Remember(phone);
        await service.Refresh(laptop.RefreshToken);

        await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(laptop.RefreshToken));

        Assert.NotNull(await service.Refresh(phone.RefreshToken));
    }

    [Fact]
    public async Task LogoutRevokesTheSignInAndIgnoresGarbage()
    {
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "a@example.com" });
        var service = Service(repository);
        var pair = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);
        repository.Remember(pair);
        var refreshed = await service.Refresh(pair.RefreshToken);

        await service.Logout("not-a-token");
        await service.Logout(refreshed.RefreshToken);

        Assert.Empty(repository.Stored);
        var error = await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(refreshed.RefreshToken));
        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public void RefreshTokensCarryAnIdTheServerStoresOnlyAsAHash()
    {
        var pair = _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);

        var principal = _tokens.Validate(pair.RefreshToken, "refresh");

        Assert.Equal(pair.RefreshTokenId, principal.FindFirst(AuthTokenService.TokenIdClaim)?.Value);
        Assert.Equal(32, AuthTokenService.HashTokenId(pair.RefreshTokenId).Length);
        Assert.NotEqual(pair.RefreshTokenId, _tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0).RefreshTokenId);
    }

    [Fact]
    public async Task UnknownEmailFailsLikeAWrongPassword()
    {
        var service = Service(new FakeAuthRepository(user: null));

        var error = await Assert.ThrowsAsync<AppProblemException>(() => service.Login("nobody@example.com", "secret"));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("Invalid credentials.", error.Message);
    }

    private static AuthService Service(AuthRepository repository) =>
        new(repository, _tokens, NullLogger<AuthService>.Instance);

    private static IOptions<AuthOptions> AuthOptionsFor(string secret, string environment)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:JwtSecret"] = secret })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        services.AddAuth(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<AuthOptions>>();
    }

    private sealed class FakeAuthRepository(User? user) : AuthRepository(null!)
    {
        private long _nextId;

        public List<RefreshToken> Stored { get; } = [];

        public void Remember(AuthTokenPair pair) => Add(new RefreshToken
        {
            UserId = user!.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = AuthTokenService.HashTokenId(pair.RefreshTokenId),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = pair.RefreshTokenExpiresAt,
        });

        public override Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(user);

        public override Task<User?> FindById(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Id == userId ? user : null);

        public override Task<RefreshToken?> FindRefreshToken(byte[] tokenHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Stored.SingleOrDefault(token => token.TokenHash.SequenceEqual(tokenHash)));

        public override Task AddRefreshToken(RefreshToken token, CancellationToken cancellationToken = default)
        {
            Add(token);
            return Task.CompletedTask;
        }

        public override Task<bool> RotateRefreshToken(long tokenId, RefreshToken successor, CancellationToken cancellationToken = default)
        {
            var current = Stored.Single(token => token.Id == tokenId);
            if (current.RotatedAt is not null)
            {
                return Task.FromResult(false);
            }

            current.RotatedAt = successor.CreatedAt;
            Add(successor);
            return Task.FromResult(true);
        }

        public override Task RevokeRefreshTokenFamily(Guid familyId, CancellationToken cancellationToken = default)
        {
            Stored.RemoveAll(token => token.FamilyId == familyId);
            return Task.CompletedTask;
        }

        private void Add(RefreshToken token)
        {
            token.Id = ++_nextId;
            Stored.Add(token);
        }
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
