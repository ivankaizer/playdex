using Api.Infrastructure.Auth;
using Api.Infrastructure.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

public sealed class AuthHardeningTests
{
    private const string StrongSecret = "auth-hardening-test-secret-at-least-thirty-two-bytes";

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
        var tokens = new AuthTokenService(Options.Create(new AuthOptions { JwtSecret = StrongSecret }));
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "a@example.com", TokenVersion = 1 });
        var service = new AuthService(repository, tokens);
        var oldPair = tokens.IssueTokenPair(7, "a@example.com", tokenVersion: 0);

        var error = await Assert.ThrowsAsync<AppProblemException>(() => service.Refresh(oldPair.RefreshToken));

        Assert.Equal(401, error.StatusCode);
    }

    [Fact]
    public async Task RefreshReissuesWithTheCurrentEmailAndVersion()
    {
        var tokens = new AuthTokenService(Options.Create(new AuthOptions { JwtSecret = StrongSecret }));
        var repository = new FakeAuthRepository(new User { Id = 7, Email = "new@example.com", TokenVersion = 2 });
        var service = new AuthService(repository, tokens);
        var pair = tokens.IssueTokenPair(7, "old@example.com", tokenVersion: 2);

        var refreshed = await service.Refresh(pair.RefreshToken);

        var principal = tokens.Validate(refreshed.AccessToken, "access");
        Assert.Equal("new@example.com", principal.FindFirst("email")?.Value);
        Assert.Equal("2", principal.FindFirst(AuthTokenService.TokenVersionClaim)?.Value);
    }

    [Fact]
    public async Task UnknownEmailFailsLikeAWrongPassword()
    {
        var tokens = new AuthTokenService(Options.Create(new AuthOptions { JwtSecret = StrongSecret }));
        var service = new AuthService(new FakeAuthRepository(user: null), tokens);

        var error = await Assert.ThrowsAsync<AppProblemException>(() => service.Login("nobody@example.com", "secret"));

        Assert.Equal(401, error.StatusCode);
        Assert.Equal("Invalid credentials.", error.Message);
    }

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
        public override Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(user);

        public override Task<User?> FindById(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Id == userId ? user : null);
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
