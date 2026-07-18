using Playdex.Api.Auth;
using Playdex.Api.Configuration;
using Playdex.Api.Data;
using Playdex.Api.Http;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class AuthTokenServiceTests
{
    private static readonly PlaydexOptions Options = new(
        "postgresql://playdex:playdex@localhost:5432/playdex",
        "auth-test-secret-that-is-long-enough-for-hmac-sha256",
        60,
        "15m",
        [],
        null,
        null,
        null);

    [Fact]
    public void IssueTokenPair_PreservesClaimsAndTokenTypes()
    {
        var service = new AuthTokenService(Options);

        var pair = service.IssueTokenPair(42, "player@example.com");
        var access = service.Validate(pair.AccessToken, "access");
        var refresh = service.Validate(pair.RefreshToken, "refresh");

        Assert.Equal("42", access.FindFirst("sub")?.Value);
        Assert.Equal("player@example.com", access.FindFirst("email")?.Value);
        Assert.Equal("access", access.FindFirst("type")?.Value);
        Assert.Equal("refresh", refresh.FindFirst("type")?.Value);
    }

    [Fact]
    public void Validate_RejectsAnAccessTokenUsedAsARefreshToken()
    {
        var service = new AuthTokenService(Options);
        var pair = service.IssueTokenPair(42, "player@example.com");

        Assert.Throws<InvalidTokenException>(() => service.Validate(pair.AccessToken, "refresh"));
    }
}

public sealed class AuthServiceTests
{
    private static readonly PlaydexOptions Options = new(
        "postgresql://playdex:playdex@localhost:5432/playdex",
        "auth-test-secret-that-is-long-enough-for-hmac-sha256",
        60,
        "15m",
        [],
        null,
        null,
        null);

    [Fact]
    public async Task Login_AcceptsAnExistingBcryptPasswordHash()
    {
        var user = new User
        {
            Id = 7,
            Email = "player@example.com",
            Password = BCrypt.Net.BCrypt.HashPassword("correct horse"),
            IsAdmin = true,
        };
        var service = new AuthService(new StubAuthRepository(user), new AuthTokenService(Options));

        var result = await service.Login("player@example.com", "correct horse");

        Assert.Equal(7, result.UserId);
        Assert.Equal("player@example.com", result.Email);
        Assert.True(result.IsAdmin);
        Assert.NotEmpty(result.AccessToken);
        Assert.NotEmpty(result.RefreshToken);
    }

    [Fact]
    public async Task Login_RejectsInvalidCredentialsWithoutRevealingWhichValueFailed()
    {
        var service = new AuthService(new StubAuthRepository(null), new AuthTokenService(Options));

        var problem = await Assert.ThrowsAsync<AppProblem>(
            () => service.Login("unknown@example.com", "wrong"));

        Assert.Equal(401, problem.StatusCode);
        Assert.Equal("Invalid credentials.", problem.Message);
    }

    private sealed class StubAuthRepository(User? user) : AuthRepository(null!)
    {
        public override Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Email == email ? user : null);

        public override Task<User?> FindById(long userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(user?.Id == userId ? user : null);
    }
}
