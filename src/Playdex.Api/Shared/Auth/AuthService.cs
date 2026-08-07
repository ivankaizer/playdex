using System.Globalization;
using Playdex.Api.Shared.Http;

namespace Playdex.Api.Shared.Auth;

public sealed record LoginResult(
    long UserId,
    string Email,
    bool IsAdmin,
    string AccessToken,
    string RefreshToken);

public sealed record RefreshResult(string AccessToken, string RefreshToken);

public sealed class AuthService(AuthRepository repository, AuthTokenService tokens)
{
    public async Task<LoginResult> Login(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await repository.FindByEmail(email, cancellationToken);
        if (user?.Password is null || !BCrypt.Net.BCrypt.Verify(password, user.Password))
        {
            throw AppProblem.Unauthorized("Invalid credentials.");
        }

        var pair = tokens.IssueTokenPair(user.Id, user.Email);
        return new LoginResult(
            user.Id,
            user.Email,
            user.IsAdmin,
            pair.AccessToken,
            pair.RefreshToken);
    }

    public async Task<RefreshResult> Refresh(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var principal = tokens.Validate(refreshToken, "refresh");
            var subject = principal.FindFirst("sub")?.Value;
            var email = principal.FindFirst("email")?.Value;
            if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId)
                || string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidTokenException();
            }

            var user = await repository.FindById(userId, cancellationToken);
            if (user is null)
            {
                throw new InvalidTokenException();
            }

            var pair = tokens.IssueTokenPair(userId, email);
            return new RefreshResult(pair.AccessToken, pair.RefreshToken);
        }
        catch (InvalidTokenException exception)
        {
            throw AppProblem.Unauthorized(exception.Message);
        }
    }
}
