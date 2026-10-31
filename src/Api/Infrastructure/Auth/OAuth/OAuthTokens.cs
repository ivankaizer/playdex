using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Api.Infrastructure.Auth.OAuth;

/// <summary>
/// Access tokens for the MCP endpoint: JWTs bound to the issuer, the resource (<c>aud</c>) and the grant (<c>gid</c>).
/// They are signed with a key derived from the JWT secret for this purpose alone, so the web API never accepts one
/// as a sign-in and the MCP endpoint never accepts a web sign-in. Authorization codes and refresh tokens are random
/// strings, stored as SHA-256 hashes.
/// </summary>
public sealed class OAuthTokens(IOptions<AuthOptions> options)
{
    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(60);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(5);

    public const string GrantIdClaim = "gid";
    public const string ClientIdClaim = "client_id";
    public const string ScopeClaim = "scope";

    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly SymmetricSecurityKey _key = new(HKDF.DeriveKey(HashAlgorithmName.SHA256,
        Encoding.UTF8.GetBytes(options.Value.JwtSecret), 32, info: "playdex:oauth-access-token"u8.ToArray()));

    public string IssueAccessToken(string issuer, string resource, long userId, Guid grantId, string clientId,
        string scope, DateTime now)
    {
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: resource,
            claims:
            [
                new(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
                new(JwtRegisteredClaimNames.Jti, NewSecret()),
                new(GrantIdClaim, grantId.ToString("D")),
                new(ClientIdClaim, clientId),
                new(ScopeClaim, scope),
            ],
            notBefore: now,
            expires: now.Add(AccessTokenLifetime),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        token.Header[JwtHeaderParameterNames.Typ] = "at+jwt";
        return _handler.WriteToken(token);
    }

    /// <summary>Signature and lifetime; the issuer and audience depend on the request and are checked by the caller.</summary>
    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = _key,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidTypes = ["at+jwt"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
    };

    public static bool IsFor(ClaimsPrincipal principal, string issuer, string resource) =>
        principal.FindFirstValue(JwtRegisteredClaimNames.Iss) == issuer
        && principal.FindAll(JwtRegisteredClaimNames.Aud).Any(aud => aud.Value == resource);

    public static string NewSecret() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    public static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    /// <summary>PKCE S256: the challenge is the base64url SHA-256 of the verifier (RFC 7636 section 4.6).</summary>
    public static bool VerifiesChallenge(string? verifier, string challenge) =>
        verifier is { Length: >= 43 and <= 128 }
        && verifier.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~')
        && CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))),
            Encoding.ASCII.GetBytes(challenge));
}
