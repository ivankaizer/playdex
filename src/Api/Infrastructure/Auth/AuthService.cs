using System.Globalization;
using Api.Infrastructure.Http;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth;

public sealed record LoginResult(
    long UserId,
    string Email,
    bool IsAdmin,
    string AccessToken,
    string RefreshToken);

public sealed record RefreshResult(string AccessToken, string RefreshToken);

public sealed partial class AuthService(AuthRepository repository, AuthTokenService tokens, ILogger<AuthService> logger)
{
    // Checked when the email is unknown, so a failed login takes as long whether or not the account exists.
    private static readonly Lazy<string> _unknownUserHash = new(() => BCrypt.Net.BCrypt.HashPassword(
        Guid.NewGuid().ToString(), UserCommand.PasswordWorkFactor));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Refresh token of user {UserId} was used twice; signed out that session")]
    private partial void LogRefreshTokenReused(long userId);

    public async Task<LoginResult> Login(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        var user = await repository.FindByEmail(email, cancellationToken);
        var verified = BCrypt.Net.BCrypt.Verify(password, user?.Password ?? _unknownUserHash.Value);
        if (user?.Password is null || !verified)
        {
            throw AppProblemException.Unauthorized("Invalid credentials.");
        }

        var pair = tokens.IssueTokenPair(user.Id, user.Email, user.TokenVersion);
        await repository.AddRefreshToken(Stored(user.Id, Guid.NewGuid(), pair), cancellationToken);
        return new LoginResult(
            user.Id,
            user.Email,
            user.IsAdmin,
            pair.AccessToken,
            pair.RefreshToken);
    }

    /// <summary>
    /// Swaps a refresh token for a new pair. Each refresh token works once: presenting one again means a copy of it is
    /// in someone else's hands, so the whole sign-in it belongs to is revoked.
    /// </summary>
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

            // Tokens issued before refresh tokens were stored have no id and were never recorded: sign in again.
            var stored = await FindStored(principal.FindFirst(AuthTokenService.TokenIdClaim)?.Value, cancellationToken);
            if (stored is null || stored.UserId != userId)
            {
                throw new InvalidTokenException();
            }

            if (stored.RotatedAt is not null)
            {
                await RevokeReused(stored, cancellationToken);
                throw new InvalidTokenException();
            }

            var user = await repository.FindById(userId, cancellationToken);
            if (user is null || user.TokenVersion != tokenVersion)
            {
                throw new InvalidTokenException();
            }

            var pair = tokens.IssueTokenPair(user.Id, user.Email, user.TokenVersion);
            if (!await repository.RotateRefreshToken(stored.Id, Stored(user.Id, stored.FamilyId, pair), cancellationToken))
            {
                await RevokeReused(stored, cancellationToken);
                throw new InvalidTokenException();
            }

            return new RefreshResult(pair.AccessToken, pair.RefreshToken);
        }
        catch (InvalidTokenException exception)
        {
            throw AppProblemException.Unauthorized(exception.Message);
        }
    }

    /// <summary>Revokes the sign-in a refresh token belongs to. Invalid, expired and already revoked tokens are ignored.</summary>
    public async Task Logout(string refreshToken, CancellationToken cancellationToken = default)
    {
        string? tokenId;
        try
        {
            tokenId = tokens.Validate(refreshToken, "refresh").FindFirst(AuthTokenService.TokenIdClaim)?.Value;
        }
        catch (InvalidTokenException)
        {
            return;
        }

        var stored = await FindStored(tokenId, cancellationToken);
        if (stored is not null)
        {
            await repository.RevokeRefreshTokenFamily(stored.FamilyId, cancellationToken);
        }
    }

    private async Task<RefreshToken?> FindStored(string? tokenId, CancellationToken cancellationToken) =>
        tokenId is null
            ? null
            : await repository.FindRefreshToken(AuthTokenService.HashTokenId(tokenId), cancellationToken);

    private async Task RevokeReused(RefreshToken stored, CancellationToken cancellationToken)
    {
        LogRefreshTokenReused(stored.UserId);
        await repository.RevokeRefreshTokenFamily(stored.FamilyId, cancellationToken);
    }

    private static RefreshToken Stored(long userId, Guid familyId, AuthTokenPair pair) => new()
    {
        UserId = userId,
        FamilyId = familyId,
        TokenHash = AuthTokenService.HashTokenId(pair.RefreshTokenId),
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = pair.RefreshTokenExpiresAt,
    };
}
