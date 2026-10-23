using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Api.Infrastructure.Common;

namespace Api.Infrastructure.Auth;

/// <param name="RefreshTokenId">The refresh token's <c>jti</c>; the server keeps only its hash (see <see cref="AuthTokenService.HashTokenId"/>).</param>
public sealed record AuthTokenPair(
    string AccessToken,
    string RefreshToken,
    string RefreshTokenId,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class AuthTokenService(IOptions<AuthOptions> options)
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(options.Value.JwtSecret));
    private readonly TimeSpan _accessTokenLifetime = ParseLifetime(options.Value.AccessTokenTtl);

    /// <summary>Claim with the user's token version; tokens from an older version are revoked (see users.token_version).</summary>
    public const string TokenVersionClaim = "ver";

    /// <summary>The refresh token's id, which the server looks up in refresh_tokens.</summary>
    public const string TokenIdClaim = JwtRegisteredClaimNames.Jti;

    public AuthTokenPair IssueTokenPair(long userId, string email, int tokenVersion)
    {
        var now = DateTime.UtcNow;
        var refreshTokenId = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return new(
            Issue(userId, email, tokenVersion, "access", now, _accessTokenLifetime, tokenId: null),
            Issue(userId, email, tokenVersion, "refresh", now, RefreshTokenLifetime, refreshTokenId),
            refreshTokenId,
            new DateTimeOffset(now.Add(RefreshTokenLifetime), TimeSpan.Zero));
    }

    public static byte[] HashTokenId(string tokenId) => SHA256.HashData(Encoding.UTF8.GetBytes(tokenId));

    public ClaimsPrincipal Validate(string token, string expectedType)
    {
        try
        {
            var principal = _handler.ValidateToken(token, ValidationParameters(), out _);
            if (!string.Equals(principal.FindFirstValue("type"), expectedType, StringComparison.Ordinal))
            {
                throw new InvalidTokenException();
            }

            return principal;
        }
        catch (InvalidTokenException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SecurityTokenException or ArgumentException)
        {
            throw new InvalidTokenException(exception);
        }
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _key,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "email",
    };

    private string Issue(
        long userId, string email, int tokenVersion, string type, DateTime now, TimeSpan lifetime, string? tokenId)
    {
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
            new(JwtRegisteredClaimNames.Email, email),
            new("type", type),
            new(TokenVersionClaim, tokenVersion.ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer32),
        ];
        if (tokenId is not null)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, tokenId));
        }

        var token = new JwtSecurityToken(
            claims: claims,
            notBefore: now,
            expires: now.Add(lifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return _handler.WriteToken(token);
    }

    private static TimeSpan ParseLifetime(string value)
    {
        if (value.Length < 2
            || !double.TryParse(value[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out var amount))
        {
            throw new Api.Infrastructure.Common.OptionsValidationException("ACCESS_TOKEN_TTL must use a numeric value followed by s, m, h, or d.");
        }

        return value[^1] switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => throw new Api.Infrastructure.Common.OptionsValidationException("ACCESS_TOKEN_TTL must use a numeric value followed by s, m, h, or d."),
        };
    }
}

public sealed class InvalidTokenException : Exception
{
    public InvalidTokenException() : base("Invalid token.") { }
    public InvalidTokenException(Exception innerException) : base("Invalid token.", innerException) { }
}
