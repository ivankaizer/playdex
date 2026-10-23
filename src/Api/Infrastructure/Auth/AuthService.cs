using System.Globalization;
using Api.Infrastructure.Http;

namespace Api.Infrastructure.Auth;

public sealed record LoginResult(
    long UserId,
    string Email,
    bool IsAdmin,
    string AccessToken,
    string RefreshToken);

public sealed record RefreshResult(string AccessToken, string RefreshToken);

public sealed class AuthService(AuthRepository repository, AuthTokenService tokens)
{
    // Checked when the email is unknown, so a failed login takes as long whether or not the account exists.
    private static readonly Lazy<string> UnknownUserHash = new(() => BCrypt.Net.BCrypt.HashPassword(
        Guid.NewGuid().ToString(), UserCommand.PasswordWorkFactor));

    public async Task<LoginResult> Login(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await repository.FindByEmail(email, cancellationToken);
        var verified = BCrypt.Net.BCrypt.Verify(password, user?.Password ?? UnknownUserHash.Value);
        if (user?.Password is null || !verified)
        {
            throw AppProblemException.Unauthorized("Invalid credentials.");
        }

        var pair = tokens.IssueTokenPair(user.Id, user.Email, user.TokenVersion);
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
            if (!long.TryParse(subject, NumberStyles.None, CultureInfo.InvariantCulture, out var userId))
            {
                throw new InvalidTokenException();
            }

            // Tokens issued before versions existed carry none and count as version 0.
            var version = principal.FindFirst(AuthTokenService.TokenVersionClaim)?.Value;
            var tokenVersion = version is null ? 0 : int.Parse(version, NumberStyles.None, CultureInfo.InvariantCulture);

            var user = await repository.FindById(userId, cancellationToken);
            if (user is null || user.TokenVersion != tokenVersion)
            {
                throw new InvalidTokenException();
            }

            var pair = tokens.IssueTokenPair(user.Id, user.Email, user.TokenVersion);
            return new RefreshResult(pair.AccessToken, pair.RefreshToken);
        }
        catch (InvalidTokenException exception)
        {
            throw AppProblemException.Unauthorized(exception.Message);
        }
    }
}
