using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;

namespace Api.Infrastructure.Auth;

/// <summary>
/// The user's API key (an active integration of type 'api') as an authentication scheme, for endpoints that clients
/// holding only the key may call: <c>.RequireAuthorization(ApiKeyAuthentication.Policy)</c>. The key is sent as
/// <c>Authorization: Bearer &lt;key&gt;</c>, like the capture routes, which still check it themselves. The principal has
/// <c>sub</c> (the user id, so <see cref="ClaimsPrincipalExtensions.GetUserId"/> works) and <c>integration_id</c>.
/// </summary>
public static class ApiKeyAuthentication
{
    public const string Scheme = "ApiKey";
    public const string Policy = "ApiKey";
    public const string IntegrationIdClaim = "integration_id";
}

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    PlaydexDbContext db)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    private const string BearerPrefix = "Bearer ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }
        var apiKey = header[BearerPrefix.Length..].Trim();
        if (apiKey.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        // As the capture routes: valid only when exactly one active API integration has this key.
        var integrations = await db.Database.SqlQuery<ApiKeyIntegrationRow>($"""
            SELECT i.id AS "Id", i.user_id AS "UserId"
            FROM integrations i
            WHERE i.api_key = {apiKey}
              AND i.type = 'api'
              AND i.active = true
            """).Take(2).ToListAsync(Context.RequestAborted);
        if (integrations.Count != 1)
        {
            return AuthenticateResult.Fail("Invalid api key.");
        }

        var integration = integrations[0];
        var identity = new ClaimsIdentity(
            [
                new Claim("sub", integration.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim(ApiKeyAuthentication.IntegrationIdClaim, integration.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ],
            ApiKeyAuthentication.Scheme);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyAuthentication.Scheme));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        var detail = Request.Headers.Authorization.Count == 0 ? "Missing api key." : "Invalid api key.";
        await Response.WriteAsJsonAsync(new ProblemDetails { Status = 401, Title = "Unauthorized", Detail = detail });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        await Response.WriteAsJsonAsync(new ProblemDetails { Status = 403, Title = "Forbidden", Detail = "Forbidden." });
    }

    private sealed class ApiKeyIntegrationRow
    {
        public long Id { get; set; }
        public long UserId { get; set; }
    }
}
