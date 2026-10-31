using Api.Infrastructure.Http;
using Microsoft.AspNetCore.WebUtilities;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth.OAuth;

/// <summary>The authorization request's parameters, as the consent page received them in its query string.</summary>
public sealed record OAuthAuthorizeRequest(string? ResponseType, string? ClientId, string? RedirectUri,
    string? CodeChallenge, string? CodeChallengeMethod, string? State, string? Scope, string? Resource);

public sealed record OAuthAuthorizeDecision(OAuthAuthorizeRequest Request, bool Approve);

/// <summary>What the consent page shows, or where it sends the browser back with an error instead.</summary>
public sealed record OAuthAuthorizeCheck(OAuthConsent? Consent, string? RedirectUrl);

public sealed record OAuthConsent(string ClientId, string ClientName, string? ClientUri, string RedirectHost,
    bool LoopbackOnly, string ResourceName, IReadOnlyList<OAuthScope> Scopes);

public sealed record OAuthRedirect(string RedirectUrl);

/// <summary>An RFC 6749 token endpoint error.</summary>
public sealed class OAuthTokenException(string error, string description) : Exception(description)
{
    public string Error { get; } = error;
}

/// <summary>An app the user connected, for the settings page.</summary>
public sealed record OAuthConnection(Guid Id, string ClientId, string ClientName, string Scope, DateTimeOffset ConnectedAt,
    DateTimeOffset? LastUsedAt);

public sealed record OAuthTokenResult(string AccessToken, int ExpiresIn, string RefreshToken, string Scope);

/// <summary>
/// The authorization code flow with PKCE (S256) for public clients identified by Client ID Metadata Documents, and
/// refresh token rotation. Approving the consent creates nothing but a code; exchanging the code creates the grant.
/// A code or refresh token presented a second time means a copy is in someone else's hands, so the grant it belongs
/// to is revoked.
/// </summary>
public sealed class OAuthService(OAuthRepository repository, IOAuthClientDirectory clients, OAuthResources resources,
    OAuthTokens tokens, TimeProvider clock)
{
    private const string OfflineAccess = "offline_access";

    public async Task<OAuthAuthorizeCheck> Check(OAuthAuthorizeRequest request, string issuer, CancellationToken ct)
    {
        var (client, approval, error) = await Validate(request, issuer, ct);
        if (error is not null)
        {
            return new(null, error.Redirect(request, issuer));
        }

        var redirect = new Uri(request.RedirectUri!);
        return new(new OAuthConsent(client.ClientId, client.Name, client.Uri, redirect.Host, client.IsLoopbackOnly,
            approval!.Resource.Name, approval.Scopes), null);
    }

    public async Task<OAuthRedirect> Decide(long userId, OAuthAuthorizeDecision decision, string issuer,
        CancellationToken ct)
    {
        var request = decision.Request;
        var (client, approval, error) = await Validate(request, issuer, ct);
        if (error is not null)
        {
            return new(error.Redirect(request, issuer));
        }

        if (!decision.Approve)
        {
            return new(new RedirectError("access_denied", "The user declined.").Redirect(request, issuer));
        }

        var code = OAuthTokens.NewSecret();
        var now = clock.GetUtcNow();
        await repository.AddCode(new OAuthAuthorizationCode
        {
            CodeHash = OAuthTokens.Hash(code),
            UserId = userId,
            ClientId = client.ClientId,
            ClientName = client.Name,
            RedirectUri = request.RedirectUri!,
            CodeChallenge = request.CodeChallenge!,
            Scope = string.Join(' ', approval!.Scopes.Select(scope => scope.Name)),
            Resource = approval.Resource.Url(issuer),
            CreatedAt = now,
            ExpiresAt = now.Add(OAuthTokens.CodeLifetime),
        }, ct);
        return new(AddQuery(request.RedirectUri!, new() { ["code"] = code, ["state"] = request.State, ["iss"] = issuer }));
    }

    public async Task<IReadOnlyList<OAuthConnection>> Connections(long userId, CancellationToken ct) =>
        (await repository.ActiveGrants(userId, ct))
        .Select(grant => new OAuthConnection(grant.Id, grant.ClientId, grant.ClientName, grant.Scope, grant.CreatedAt,
            grant.LastUsedAt))
        .ToArray();

    /// <summary>Revokes a connection: its refresh tokens stop working at once, and so do its access tokens.</summary>
    public async Task Disconnect(long userId, Guid id, CancellationToken ct)
    {
        if (await repository.RevokeGrant(userId, id, clock.GetUtcNow(), ct) == 0)
        {
            throw AppProblemException.NotFound("Connection not found.");
        }
    }

    public Task<OAuthTokenResult> Token(IFormCollection form, string issuer, CancellationToken ct)
    {
        var requested = form["resource"].ToString();
        if (requested.Length > 0 && resources.ForUrl(requested, issuer) is null)
        {
            throw new OAuthTokenException("invalid_target", "This server issues no tokens for that resource.");
        }

        return form["grant_type"].ToString() switch
        {
            "authorization_code" => ExchangeCode(form, issuer, requested, ct),
            "refresh_token" => Refresh(form, issuer, requested, ct),
            _ => throw new OAuthTokenException("unsupported_grant_type", "Use authorization_code or refresh_token."),
        };
    }

    /// <param name="resource">The resource the request named, if any: it has to be the one the code or grant is for.</param>
    private async Task<OAuthTokenResult> ExchangeCode(IFormCollection form, string issuer, string resource, CancellationToken ct)
    {
        var code = form["code"].ToString();
        if (code.Length is 0 or > 128)
        {
            throw new OAuthTokenException("invalid_request", "The code is missing.");
        }

        var now = clock.GetUtcNow();
        var (result, error) = await repository.InTransaction<(OAuthTokenResult?, OAuthTokenException?)>(async () =>
        {
            var stored = await repository.LockCode(OAuthTokens.Hash(code), ct);
            if (stored is null || stored.ExpiresAt <= now)
            {
                return (null, InvalidGrant("The code is invalid or expired."));
            }

            if (stored.UsedAt is not null)
            {
                if (stored.GrantId is { } issued)
                {
                    await repository.RevokeGrant(issued, now, ct);
                }

                return (null, InvalidGrant("The code was already used."));
            }

            if (stored.ClientId != form["client_id"].ToString() || stored.RedirectUri != form["redirect_uri"].ToString())
            {
                return (null, InvalidGrant("The code was issued to another client or redirect URI."));
            }

            if (!OAuthTokens.VerifiesChallenge(form["code_verifier"].ToString(), stored.CodeChallenge))
            {
                return (null, InvalidGrant("The code_verifier does not match the code_challenge."));
            }

            if (resource.Length > 0 && !OAuthServer.IsResource(resource, stored.Resource))
            {
                return (null, InvalidGrant("The code was issued for another resource."));
            }

            var version = await repository.TokenVersion(stored.UserId, ct);
            if (version is null)
            {
                return (null, InvalidGrant("The account no longer exists."));
            }

            var grant = new OAuthGrant
            {
                Id = Guid.NewGuid(),
                UserId = stored.UserId,
                ClientId = stored.ClientId,
                ClientName = stored.ClientName,
                Scope = stored.Scope,
                Resource = stored.Resource,
                TokenVersion = version.Value,
                CreatedAt = now,
                LastUsedAt = now,
            };
            repository.Add(grant);
            stored.UsedAt = now;
            stored.GrantId = grant.Id;
            return (Issue(grant, issuer, now), null);
        }, ct);
        return result ?? throw error!;
    }

    private async Task<OAuthTokenResult> Refresh(IFormCollection form, string issuer, string resource, CancellationToken ct)
    {
        var presented = form["refresh_token"].ToString();
        if (presented.Length is 0 or > 128)
        {
            throw new OAuthTokenException("invalid_request", "The refresh_token is missing.");
        }

        var now = clock.GetUtcNow();
        var (result, error) = await repository.InTransaction<(OAuthTokenResult?, OAuthTokenException?)>(async () =>
        {
            var stored = await repository.LockRefreshToken(OAuthTokens.Hash(presented), ct);
            if (stored is null || stored.ExpiresAt <= now)
            {
                return (null, InvalidGrant("The refresh token is invalid or expired."));
            }

            if (stored.RotatedAt is not null)
            {
                await repository.RevokeGrant(stored.GrantId, now, ct);
                return (null, InvalidGrant("The refresh token was already used."));
            }

            var grant = await repository.FindGrant(stored.GrantId, ct);
            if (grant is null || grant.RevokedAt is not null
                || (resource.Length > 0 && !OAuthServer.IsResource(resource, grant.Resource))
                || grant.ClientId != form["client_id"].ToString()
                || await repository.TokenVersion(grant.UserId, ct) != grant.TokenVersion)
            {
                return (null, InvalidGrant("The connection was revoked."));
            }

            stored.RotatedAt = now;
            grant.LastUsedAt = now;
            return (Issue(grant, issuer, now), null);
        }, ct);
        return result ?? throw error!;
    }

    private OAuthTokenResult Issue(OAuthGrant grant, string issuer, DateTimeOffset now)
    {
        var refresh = OAuthTokens.NewSecret();
        repository.Add(new OAuthRefreshToken
        {
            GrantId = grant.Id,
            TokenHash = OAuthTokens.Hash(refresh),
            CreatedAt = now,
            ExpiresAt = now.Add(OAuthTokens.RefreshTokenLifetime),
        });
        var access = tokens.IssueAccessToken(issuer, grant.Resource, grant.UserId, grant.Id, grant.ClientId, grant.Scope,
            now.UtcDateTime);
        return new(access, (int)OAuthTokens.AccessTokenLifetime.TotalSeconds, refresh, grant.Scope);
    }

    /// <summary>
    /// The client and redirect URI first: until both are known, an error cannot be sent back to the client and is
    /// shown to the user instead (RFC 6749 section 4.1.2.1). Every later error goes back to the client.
    /// </summary>
    private async Task<(OAuthClient Client, Approval? Approval, RedirectError? Error)> Validate(
        OAuthAuthorizeRequest request, string issuer, CancellationToken ct)
    {
        OAuthClient client;
        try { client = await clients.Find(request.ClientId, ct); }
        catch (OAuthClientException exception) { throw AppProblemException.Validation(exception.Message); }

        if (!client.AllowsRedirect(request.RedirectUri))
        {
            throw AppProblemException.Validation($"{client.Name} did not register this redirect URI.");
        }

        if (request.ResponseType != "code")
        {
            return (client, null, new("unsupported_response_type", "Only the code response type is supported."));
        }

        if (request.CodeChallengeMethod != "S256" || request.CodeChallenge is not { Length: 43 })
        {
            return (client, null, new("invalid_request", "PKCE with code_challenge_method S256 is required."));
        }

        if (request.State is { Length: > 1024 })
        {
            return (client, null, new("invalid_request", "The state is too long."));
        }

        // A client names the resource (RFC 8707); one that does not gets the only one there is.
        var resource = string.IsNullOrEmpty(request.Resource)
            ? resources.All.Count == 1 ? resources.All[0] : null
            : resources.ForUrl(request.Resource, issuer);
        if (resource is null)
        {
            return (client, null, new("invalid_target", "Name a resource this server issues tokens for."));
        }

        // No scope asks for all of the resource's. offline_access is not a scope here: every grant gets a refresh token.
        var requested = (request.Scope ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(scope => scope != OfflineAccess).Distinct(StringComparer.Ordinal).ToArray();
        var scopes = requested.Length == 0
            ? resource.Scopes
            : resource.Scopes.Where(scope => requested.Contains(scope.Name)).ToArray();
        if (scopes.Count == 0 || (requested.Length > 0 && scopes.Count != requested.Length))
        {
            return (client, null, new("invalid_scope", $"{resource.Name} offers the scopes: {resource.ScopeList}."));
        }

        return (client, new(resource, scopes), null);
    }

    private sealed record Approval(OAuthResource Resource, IReadOnlyList<OAuthScope> Scopes);

    private static OAuthTokenException InvalidGrant(string description) => new("invalid_grant", description);

    private sealed record RedirectError(string Error, string Description)
    {
        public string Redirect(OAuthAuthorizeRequest request, string issuer) => AddQuery(request.RedirectUri!, new()
        {
            ["error"] = Error,
            ["error_description"] = Description,
            ["state"] = request.State,
            ["iss"] = issuer,
        });
    }

    private static string AddQuery(string uri, Dictionary<string, string?> values) =>
        QueryHelpers.AddQueryString(uri, values.Where(pair => pair.Value is not null)
            .Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
}
