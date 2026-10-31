namespace Playdex.Persistence;

/// <summary>
/// An OAuth client the user connected to their account, such as Claude or ChatGPT: what the consent screen
/// approved. Its refresh tokens live as long as it is not revoked, and the user's token version still matches.
/// </summary>
public sealed class OAuthGrant
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    /// <summary>The client's Client ID Metadata Document URL.</summary>
    public required string ClientId { get; set; }
    public required string ClientName { get; set; }
    public required string Scope { get; set; }
    /// <summary>The protected resource (the MCP endpoint's URL) its access tokens are for.</summary>
    public required string Resource { get; set; }
    public int TokenVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>An authorization code: single use, short-lived, stored only as a hash.</summary>
public sealed class OAuthAuthorizationCode
{
    public required byte[] CodeHash { get; set; }
    public long UserId { get; set; }
    public required string ClientId { get; set; }
    public required string ClientName { get; set; }
    public required string RedirectUri { get; set; }
    /// <summary>The PKCE S256 challenge the token request's verifier must match.</summary>
    public required string CodeChallenge { get; set; }
    public required string Scope { get; set; }
    public required string Resource { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    /// <summary>The grant the code was exchanged for, revoked if the code is presented again.</summary>
    public Guid? GrantId { get; set; }
}

public sealed class OAuthRefreshToken
{
    public long Id { get; set; }
    public Guid GrantId { get; set; }
    public required byte[] TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RotatedAt { get; set; }
}
