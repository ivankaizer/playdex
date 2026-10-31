using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Api.Infrastructure.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Api.Infrastructure.Auth.OAuth;

public static class OAuthEndpoints
{
    /// <summary>Discovery and the token endpoint answer any origin: browser-based clients call them too.</summary>
    public const string CorsPolicy = "oauth-public";

    private static readonly string[] _responseTypes = ["code"];
    private static readonly string[] _grantTypes = ["authorization_code", "refresh_token"];
    private static readonly string[] _codeChallengeMethods = ["S256"];
    private static readonly string[] _clientAuthMethods = ["none"];
    private static readonly string[] _bearerMethods = ["header"];

    public static IServiceCollection AddOAuthServer(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        services.AddSingleton<OAuthTokens>();
        services.AddSingleton<OAuthResources>();
        services.AddScoped<OAuthRepository>();
        services.AddScoped<OAuthService>();
        services.AddHttpClient<IOAuthClientDirectory, ClientMetadataDocuments>(ClientMetadataDocuments.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(ClientMetadataDocuments.CreateHandler);
        services.AddCors(cors => cors.AddPolicy(CorsPolicy, policy =>
            policy.AllowAnyOrigin().WithMethods("GET", "POST").WithHeaders("Content-Type", "MCP-Protocol-Version")));

        services.AddAuthentication()
            .AddJwtBearer(OAuthServer.AccessTokenScheme)
            .AddPolicyScheme(OAuthServer.ApiKeyOrAccessTokenScheme, null, options =>
            {
                options.ForwardDefaultSelector = context =>
                    context.Request.Headers.Authorization.ToString().StartsWith("Bearer plx_", StringComparison.OrdinalIgnoreCase)
                        ? ApiKeyAuthentication.Scheme
                        : OAuthServer.AccessTokenScheme;
                // A client learns from the challenge where to sign in, whichever credential it sent.
                options.ForwardChallenge = OAuthServer.AccessTokenScheme;
            });
        services.AddOptions<JwtBearerOptions>(OAuthServer.AccessTokenScheme)
            .Configure<OAuthTokens>((options, tokens) =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = tokens.ValidationParameters();
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateGrant,
                    OnChallenge = Challenge,
                };
            });
        services.AddAuthorizationBuilder()
            .AddPolicy(OAuthServer.AccessTokenPolicy,
                policy => policy.AddAuthenticationSchemes(OAuthServer.AccessTokenScheme).RequireAuthenticatedUser())
            .AddPolicy(OAuthServer.ApiKeyOrAccessTokenPolicy,
                policy => policy.AddAuthenticationSchemes(OAuthServer.ApiKeyOrAccessTokenScheme).RequireAuthenticatedUser());
        return services;
    }

    /// <summary>The discovery documents, at the origin's root where RFC 8414 and RFC 9728 put them.</summary>
    public static void MapOAuthDiscovery(this IEndpointRouteBuilder app)
    {
        app.MapGet(OAuthServer.AuthorizationServerMetadataPath, (HttpRequest request, OAuthResources resources) =>
        {
            var issuer = OAuthServer.Issuer(request);
            return TypedResults.Json(new Dictionary<string, object>
            {
                ["issuer"] = issuer,
                ["authorization_endpoint"] = issuer + OAuthServer.AuthorizationPath,
                ["token_endpoint"] = issuer + OAuthServer.TokenPath,
                ["response_types_supported"] = _responseTypes,
                ["grant_types_supported"] = _grantTypes,
                ["code_challenge_methods_supported"] = _codeChallengeMethods,
                ["token_endpoint_auth_methods_supported"] = _clientAuthMethods,
                ["scopes_supported"] = resources.All.SelectMany(r => r.Scopes).Select(s => s.Name).Distinct().ToArray(),
                ["client_id_metadata_document_supported"] = true,
                ["authorization_response_iss_parameter_supported"] = true,
            });
        }).RequireCors(CorsPolicy);

        // RFC 9728 section 3.1: a resource at /api/x describes itself at /.well-known/oauth-protected-resource/api/x.
        // The root document is the only resource's, when there is one.
        app.MapGet(OAuthServer.ProtectedResourceMetadataPath + "/{**path}", (string? path, HttpRequest request, OAuthResources resources) =>
            ProtectedResource(request, resources.All.FirstOrDefault(r => string.Equals(r.Path, "/" + path, StringComparison.OrdinalIgnoreCase))))
            .RequireCors(CorsPolicy);
        app.MapGet(OAuthServer.ProtectedResourceMetadataPath, (HttpRequest request, OAuthResources resources) =>
            ProtectedResource(request, resources.All.Count == 1 ? resources.All[0] : null))
            .RequireCors(CorsPolicy);
    }

    public static void MapOAuthEndpoints(this RouteGroupBuilder api)
    {
        var oauth = api.MapGroup("/oauth");
        // The consent page: signed in to the web app, it asks what to show, then sends the user's decision.
        oauth.MapPost("/authorize/check", async (OAuthAuthorizeRequest request, HttpContext http, OAuthService service) =>
            TypedResults.Ok(await service.Check(request, OAuthServer.Issuer(http.Request), http.RequestAborted)))
            .RequireAuthorization().RequireRateLimiting(RateLimiting.Refresh);
        oauth.MapPost("/authorize", async (OAuthAuthorizeDecision decision, HttpContext http, OAuthService service) =>
            TypedResults.Ok(await service.Decide(http.User.GetUserId(), decision, OAuthServer.Issuer(http.Request),
                http.RequestAborted)))
            .RequireAuthorization().RequireRateLimiting(RateLimiting.Refresh);
        oauth.MapGet("/connections", async (HttpContext http, OAuthService service) =>
            TypedResults.Ok(await service.Connections(http.User.GetUserId(), http.RequestAborted)))
            .RequireAuthorization();
        oauth.MapDelete("/connections/{id:guid}", async (Guid id, HttpContext http, OAuthService service) =>
        {
            await service.Disconnect(http.User.GetUserId(), id, http.RequestAborted);
            return TypedResults.NoContent();
        }).RequireAuthorization();
        oauth.MapPost("/token", Token).RequireCors(CorsPolicy).RequireRateLimiting(RateLimiting.Refresh);
    }

    private static IResult ProtectedResource(HttpRequest request, OAuthResource? resource)
    {
        if (resource is null)
        {
            return TypedResults.NotFound();
        }

        var issuer = OAuthServer.Issuer(request);
        return TypedResults.Json(new Dictionary<string, object>
        {
            ["resource"] = resource.Url(issuer),
            ["authorization_servers"] = new[] { issuer },
            ["scopes_supported"] = resource.Scopes.Select(scope => scope.Name).ToArray(),
            ["bearer_methods_supported"] = _bearerMethods,
            ["resource_name"] = resource.Name,
        });
    }

    /// <summary>RFC 6749 section 5: a form in, plain JSON out (ApiEnvelopeMiddleware leaves this path alone).</summary>
    private static async Task<IResult> Token(HttpContext http, OAuthService service)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        if (!http.Request.HasFormContentType)
        {
            return TokenError("invalid_request", "Send the token request as application/x-www-form-urlencoded.");
        }

        IFormCollection form;
        try { form = await http.Request.ReadFormAsync(http.RequestAborted); }
        catch (InvalidDataException) { return TokenError("invalid_request", "The token request is malformed."); }

        try
        {
            var result = await service.Token(form, OAuthServer.Issuer(http.Request), http.RequestAborted);
            return TypedResults.Json(new Dictionary<string, object>
            {
                ["access_token"] = result.AccessToken,
                ["token_type"] = "Bearer",
                ["expires_in"] = result.ExpiresIn,
                ["refresh_token"] = result.RefreshToken,
                ["scope"] = result.Scope,
            });
        }
        catch (OAuthTokenException exception)
        {
            return TokenError(exception.Error, exception.Message);
        }
    }

    private static JsonHttpResult<Dictionary<string, string>> TokenError(string error, string description) => TypedResults.Json(
        new Dictionary<string, string> { ["error"] = error, ["error_description"] = description },
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>
    /// A token is accepted only by the resource it was issued for (RFC 8707: its <c>aud</c> is that resource's URL),
    /// with scopes that resource offers, while its grant stands.
    /// </summary>
    private static async Task ValidateGrant(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        var http = context.HttpContext;
        var resource = http.RequestServices.GetRequiredService<OAuthResources>().ForPath(http.Request.Path);
        var issuer = OAuthServer.Issuer(http.Request);
        var scopes = principal.FindFirstValue(OAuthTokens.ScopeClaim)?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (resource is null
            || !OAuthTokens.IsFor(principal, issuer, resource.Url(issuer))
            || scopes.Length == 0
            || !scopes.All(scope => resource.Scopes.Any(offered => offered.Name == scope))
            || !Guid.TryParse(principal.FindFirstValue(OAuthTokens.GrantIdClaim), out var grantId)
            || !long.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var userId))
        {
            context.Fail("Invalid token.");
            return;
        }

        if (!await http.RequestServices.GetRequiredService<OAuthRepository>().IsGrantActive(grantId, userId, http.RequestAborted))
        {
            context.Fail("The connection was revoked.");
        }
    }

    /// <summary>RFC 9728 section 5.1: the 401 tells the client where the resource's metadata, and so its authorization server, is.</summary>
    private static async Task Challenge(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var request = context.Request;
        var resource = context.HttpContext.RequestServices.GetRequiredService<OAuthResources>().ForPath(request.Path);
        var parameters = new List<string>();
        if (resource is not null)
        {
            var issuer = OAuthServer.Issuer(request);
            parameters.Add($"resource_metadata=\"{resource.MetadataUrl(issuer)}\"");
            parameters.Add($"scope=\"{resource.ScopeList}\"");
        }

        if (request.Headers.Authorization.Count > 0)
        {
            parameters.Add("error=\"invalid_token\"");
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = parameters.Count == 0 ? "Bearer" : "Bearer " + string.Join(", ", parameters);
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 401,
            Title = "Unauthorized",
            Detail = request.Headers.Authorization.Count == 0 ? "Sign in to Playdex to connect." : "Invalid token.",
        });
    }
}
