using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Api.Shared.Common;

namespace Api.Shared.Auth;

public sealed record AuthTokenPair(string AccessToken, string RefreshToken);

public sealed class AuthTokenService(IOptions<AuthOptions> options)
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);
    private readonly JwtSecurityTokenHandler _handler = new() { MapInboundClaims = false };
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(options.Value.JwtSecret));
    private readonly TimeSpan _accessTokenLifetime = ParseLifetime(options.Value.AccessTokenTtl);

    public AuthTokenPair IssueTokenPair(long userId, string email) =>
        new(
            Issue(userId, email, "access", _accessTokenLifetime),
            Issue(userId, email, "refresh", RefreshTokenLifetime));

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
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "email",
    };

    private string Issue(long userId, string email, string type, TimeSpan lifetime)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim("type", type),
            ],
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
            throw new Api.Shared.Common.OptionsValidationException("ACCESS_TOKEN_TTL must use a numeric value followed by s, m, h, or d.");
        }

        return value[^1] switch
        {
            's' => TimeSpan.FromSeconds(amount),
            'm' => TimeSpan.FromMinutes(amount),
            'h' => TimeSpan.FromHours(amount),
            'd' => TimeSpan.FromDays(amount),
            _ => throw new Api.Shared.Common.OptionsValidationException("ACCESS_TOKEN_TTL must use a numeric value followed by s, m, h, or d."),
        };
    }
}

public sealed class InvalidTokenException : Exception
{
    public InvalidTokenException() : base("Invalid token.") { }
    public InvalidTokenException(Exception innerException) : base("Invalid token.", innerException) { }
}
