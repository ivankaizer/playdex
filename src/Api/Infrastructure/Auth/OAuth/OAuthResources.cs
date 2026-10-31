using Microsoft.Extensions.Options;

namespace Api.Infrastructure.Auth.OAuth;

/// <summary>A permission a resource grants, with the sentence the consent page shows for it.</summary>
public sealed record OAuthScope(string Name, string Description);

/// <summary>
/// An API that accepts OAuth access tokens: the path it is mapped at (its URL on the issuer's origin is the token's
/// audience), a name for the consent page and the scopes a client can ask for.
/// </summary>
public sealed record OAuthResource(string Path, string Name, IReadOnlyList<OAuthScope> Scopes)
{
    public string Url(string issuer) => issuer + Path;

    public string MetadataUrl(string issuer) => issuer + OAuthServer.ProtectedResourceMetadataPath + Path;

    public string ScopeList => string.Join(' ', Scopes.Select(scope => scope.Name));
}

public sealed class OAuthResourceOptions
{
    public List<OAuthResource> Resources { get; } = [];
}

/// <summary>The resources modules registered with <see cref="OAuthResourceRegistration.AddOAuthResource"/>.</summary>
public sealed class OAuthResources(IOptions<OAuthResourceOptions> options)
{
    public IReadOnlyList<OAuthResource> All => options.Value.Resources;

    /// <summary>The resource a request is for: the one whose path the request path is in.</summary>
    public OAuthResource? ForPath(PathString path) =>
        All.FirstOrDefault(resource => path.StartsWithSegments(resource.Path, StringComparison.OrdinalIgnoreCase));

    /// <summary>The resource a client named in its <c>resource</c> parameter (RFC 8707).</summary>
    public OAuthResource? ForUrl(string? url, string issuer) =>
        All.FirstOrDefault(resource => OAuthServer.IsResource(url, resource.Url(issuer)));
}

public static class OAuthResourceRegistration
{
    /// <summary>Lets <paramref name="resource"/> accept OAuth access tokens issued for it.</summary>
    public static IServiceCollection AddOAuthResource(this IServiceCollection services, OAuthResource resource) =>
        services.Configure<OAuthResourceOptions>(options => options.Resources.Add(resource));
}
