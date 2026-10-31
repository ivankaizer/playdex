using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Api.Infrastructure.Auth.OAuth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Api.Tests;

/// <summary>
/// The OAuth flow a client runs against Playdex: discovery from a protected resource's 401, consent, code exchange
/// with PKCE, refresh rotation and what a replayed code or refresh token revokes. The resources are two probes this
/// test maps (<see cref="Probes"/>), and client metadata documents come from <see cref="FakeClients"/>.
/// </summary>
[Collection(AdminApiCollectionDefinition.Name)]
public sealed class OAuthIntegrationTests(AdminApiFixture fixture)
{
    private const string ClientId = "https://assistant.example/oauth/client.json";
    private const string RedirectUri = "http://127.0.0.1:53682/callback";
    private const string ProbePath = "/api/oauth-probe";
    private const string OtherProbePath = "/api/oauth-probe-other";
    private const string Resource = "http://localhost" + ProbePath;

    [Fact]
    public async Task ChallengePointsToTheMetadataAndTheAuthorizationServer()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var client = factory.CreateClient();

        using var unauthorized = await client.GetAsync(ProbePath);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        var challenge = unauthorized.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/api/oauth-probe\"", challenge);
        Assert.Contains("scope=\"probe:read\"", challenge);

        using var resource = await JsonOf(client, "/.well-known/oauth-protected-resource/api/oauth-probe");
        Assert.Equal(Resource, resource.RootElement.GetProperty("resource").GetString());
        Assert.Equal("Probe", resource.RootElement.GetProperty("resource_name").GetString());
        using var missing = await client.GetAsync("/.well-known/oauth-protected-resource/api/nothing");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("http://localhost", resource.RootElement.GetProperty("authorization_servers")[0].GetString());

        using var server = await JsonOf(client, "/.well-known/oauth-authorization-server");
        var metadata = server.RootElement;
        Assert.Equal("http://localhost", metadata.GetProperty("issuer").GetString());
        Assert.Equal("http://localhost/oauth/authorize", metadata.GetProperty("authorization_endpoint").GetString());
        Assert.Equal("http://localhost/api/oauth/token", metadata.GetProperty("token_endpoint").GetString());
        Assert.True(metadata.GetProperty("client_id_metadata_document_supported").GetBoolean());
        Assert.True(metadata.GetProperty("authorization_response_iss_parameter_supported").GetBoolean());
        Assert.Equal(["none"], metadata.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray().Select(e => e.GetString()));
        Assert.Equal(["S256"], metadata.GetProperty("code_challenge_methods_supported").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("probe:read", metadata.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ConsentShowsTheClientAndRejectsUnregisteredRedirects()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var web = await SignIn(factory);
        var (_, challenge) = Pkce();

        var check = await Data(await web.PostAsJsonAsync("/api/oauth/authorize/check", Request(challenge)));
        var consent = check.GetProperty("consent");
        Assert.Equal("Test Assistant", consent.GetProperty("clientName").GetString());
        Assert.Equal("127.0.0.1", consent.GetProperty("redirectHost").GetString());
        Assert.True(consent.GetProperty("loopbackOnly").GetBoolean());
        Assert.Equal("Probe", consent.GetProperty("resourceName").GetString());
        var scope = Assert.Single(consent.GetProperty("scopes").EnumerateArray().ToArray());
        Assert.Equal("Read the probe", scope.GetProperty("description").GetString());

        var unknownScope = await Data(await web.PostAsJsonAsync("/api/oauth/authorize/check",
            Request(challenge) with { Scope = "probe:read admin" }));
        Assert.Equal("invalid_scope", Query(unknownScope.GetProperty("redirectUrl").GetString()!)["error"]);
        var otherResource = await Data(await web.PostAsJsonAsync("/api/oauth/authorize/check",
            Request(challenge) with { Resource = "https://elsewhere.example/api" }));
        Assert.Equal("invalid_target", Query(otherResource.GetProperty("redirectUrl").GetString()!)["error"]);

        // An unregistered redirect is shown to the user, never followed.
        using var foreign = await web.PostAsJsonAsync("/api/oauth/authorize/check",
            Request(challenge) with { RedirectUri = "https://attacker.example/callback" });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);

        // A request without PKCE goes back to the client with an error, carrying state and iss.
        var noPkce = await Data(await web.PostAsJsonAsync("/api/oauth/authorize/check",
            Request(challenge) with { CodeChallengeMethod = "plain" }));
        var back = Query(noPkce.GetProperty("redirectUrl").GetString()!);
        Assert.Equal("invalid_request", back["error"]);
        Assert.Equal("xyz", back["state"]);
        Assert.Equal("http://localhost", back["iss"]);

        var denied = await Data(await web.PostAsJsonAsync("/api/oauth/authorize", new { request = Request(challenge), approve = false }));
        Assert.Equal("access_denied", Query(denied.GetProperty("redirectUrl").GetString()!)["error"]);

        using var anonymous = factory.CreateClient();
        using var unauthenticated = await anonymous.PostAsJsonAsync("/api/oauth/authorize/check", Request(challenge));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
    }

    [Fact]
    public async Task ApprovedCodeBecomesTokensForItsResourceOnly()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var web = await SignIn(factory);
        var (verifier, challenge) = Pkce();
        var code = await Approve(web, challenge);
        using var anonymous = factory.CreateClient();

        var wrongVerifier = await TokenRequest(anonymous, CodeForm(code, Pkce().Verifier));
        Assert.Equal("invalid_grant", wrongVerifier.GetProperty("error").GetString());

        var tokens = await TokenRequest(anonymous, CodeForm(code, verifier));
        var accessToken = tokens.GetProperty("access_token").GetString()!;
        Assert.Equal("Bearer", tokens.GetProperty("token_type").GetString());
        Assert.Equal(3600, tokens.GetProperty("expires_in").GetInt32());
        Assert.Equal("probe:read", tokens.GetProperty("scope").GetString());

        Assert.Equal(HttpStatusCode.OK, await ProbeStatus(factory, ProbePath, accessToken));
        // The token's audience is the probe: another resource and the web API refuse it.
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, OtherProbePath, accessToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, "/api/me", accessToken));

        // Replaying the code revokes the connection it created.
        var replay = await TokenRequest(anonymous, CodeForm(code, verifier));
        Assert.Equal("invalid_grant", replay.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, ProbePath, accessToken));
    }

    [Fact]
    public async Task WebSignInsAndExpiredTokensAreRefused()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var web = await SignIn(factory);
        var jwt = web.DefaultRequestHeaders.Authorization!.Parameter!;
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, ProbePath, jwt));

        var (verifier, challenge) = Pkce();
        using var anonymous = factory.CreateClient();
        await TokenRequest(anonymous, CodeForm(await Approve(web, challenge), verifier));
        await using var db = fixture.CreateDb();
        var grant = db.OAuthGrants.Where(g => g.UserId == fixture.UserId && g.RevokedAt == null && g.Resource == Resource)
            .OrderByDescending(g => g.CreatedAt).First();
        var expired = factory.Services.GetRequiredService<OAuthTokens>().IssueAccessToken("http://localhost", Resource,
            fixture.UserId, grant.Id, ClientId, "probe:read", DateTime.UtcNow.AddHours(-2));
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, ProbePath, expired));
        var current = factory.Services.GetRequiredService<OAuthTokens>().IssueAccessToken("http://localhost", Resource,
            fixture.UserId, grant.Id, ClientId, "probe:read", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.OK, await ProbeStatus(factory, ProbePath, current));
        // A scope the resource does not offer makes the token invalid there.
        var foreignScope = factory.Services.GetRequiredService<OAuthTokens>().IssueAccessToken("http://localhost", Resource,
            fixture.UserId, grant.Id, ClientId, "probe:read admin", DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, ProbePath, foreignScope));
    }

    [Fact]
    public async Task RefreshTokensRotateAndAReplayRevokesTheConnection()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var web = await SignIn(factory);
        var (verifier, challenge) = Pkce();
        using var anonymous = factory.CreateClient();
        var first = await TokenRequest(anonymous, CodeForm(await Approve(web, challenge), verifier));
        var firstRefresh = first.GetProperty("refresh_token").GetString()!;

        var second = await TokenRequest(anonymous, RefreshForm(firstRefresh));
        var secondAccess = second.GetProperty("access_token").GetString()!;
        Assert.NotEqual(firstRefresh, second.GetProperty("refresh_token").GetString());
        Assert.Equal(HttpStatusCode.OK, await ProbeStatus(factory, ProbePath, secondAccess));

        var otherClient = await TokenRequest(anonymous, RefreshForm(second.GetProperty("refresh_token").GetString()!)
            .Append(new("client_id", "https://other.example/client.json")).ToArray());
        Assert.Equal("invalid_grant", otherClient.GetProperty("error").GetString());

        var replay = await TokenRequest(anonymous, RefreshForm(firstRefresh));
        Assert.Equal("invalid_grant", replay.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, ProbePath, secondAccess));
        var afterRevoke = await TokenRequest(anonymous, RefreshForm(second.GetProperty("refresh_token").GetString()!));
        Assert.Equal("invalid_grant", afterRevoke.GetProperty("error").GetString());
    }

    [Fact]
    public async Task ConnectionsAreListedAndDisconnectingRevokesTheirTokens()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var web = await SignIn(factory);
        var (verifier, challenge) = Pkce();
        using var anonymous = factory.CreateClient();
        var tokens = await TokenRequest(anonymous, CodeForm(await Approve(web, challenge), verifier));
        var accessToken = tokens.GetProperty("access_token").GetString()!;

        var connections = (await Data(await web.GetAsync("/api/oauth/connections"))).EnumerateArray().ToArray();
        var connection = connections.First();
        Assert.Equal("Test Assistant", connection.GetProperty("clientName").GetString());
        Assert.Equal(ClientId, connection.GetProperty("clientId").GetString());
        var id = connection.GetProperty("id").GetGuid();

        // Someone else's connection is not theirs to see or revoke.
        using (var admin = await fixture.LoginAsync(AdminApiFixture.AdminEmail))
        {
            Assert.DoesNotContain((await Data(await admin.GetAsync("/api/oauth/connections"))).EnumerateArray(),
                other => other.GetProperty("id").GetGuid() == id);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/oauth/connections/{id}")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.OK, await ProbeStatus(factory, ProbePath, accessToken));

        Assert.Equal(HttpStatusCode.NoContent, (await web.DeleteAsync($"/api/oauth/connections/{id}")).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, await ProbeStatus(factory, ProbePath, accessToken));
        Assert.Equal("invalid_grant", (await TokenRequest(anonymous, RefreshForm(tokens.GetProperty("refresh_token").GetString()!)))
            .GetProperty("error").GetString());
        Assert.DoesNotContain((await Data(await web.GetAsync("/api/oauth/connections"))).EnumerateArray(),
            other => other.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task TokenEndpointSpeaksOAuthErrors()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var factory = WithFakeClients();
        using var anonymous = factory.CreateClient();

        Assert.Equal("unsupported_grant_type",
            (await TokenRequest(anonymous, [new("grant_type", "password")])).GetProperty("error").GetString());
        Assert.Equal("invalid_target", (await TokenRequest(anonymous,
            [new("grant_type", "refresh_token"), new("refresh_token", "x"), new("resource", "https://elsewhere.example/mcp")]))
            .GetProperty("error").GetString());
        using var json = await anonymous.PostAsJsonAsync("/api/oauth/token", new { grant_type = "refresh_token" });
        Assert.Equal(HttpStatusCode.BadRequest, json.StatusCode);
    }

    private WebApplicationFactory<Program> WithFakeClients() => fixture.Factory!.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IOAuthClientDirectory, FakeClients>();
            services.AddOAuthResource(new OAuthResource(ProbePath, "Probe", [new("probe:read", "Read the probe")]));
            services.AddOAuthResource(new OAuthResource(OtherProbePath, "Other probe", [new("probe:read", "Read the other probe")]));
            services.AddSingleton<IStartupFilter, Probes>();
        }));

    private static async Task<HttpClient> SignIn(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/login",
            new { email = AdminApiFixture.UserEmail, password = AdminApiFixture.Password });
        var token = (await Data(response)).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static OAuthAuthorizeRequest Request(string challenge) =>
        new("code", ClientId, RedirectUri, challenge, "S256", "xyz", "probe:read offline_access", Resource);

    private static async Task<string> Approve(HttpClient web, string challenge)
    {
        var redirect = await Data(await web.PostAsJsonAsync("/api/oauth/authorize", new { request = Request(challenge), approve = true }));
        var url = redirect.GetProperty("redirectUrl").GetString()!;
        Assert.StartsWith(RedirectUri + "?", url);
        var query = Query(url);
        Assert.Equal("xyz", query["state"]);
        Assert.Equal("http://localhost", query["iss"]);
        return query["code"]!;
    }

    private static KeyValuePair<string, string>[] CodeForm(string code, string verifier) =>
    [
        new("grant_type", "authorization_code"),
        new("code", code),
        new("redirect_uri", RedirectUri),
        new("client_id", ClientId),
        new("code_verifier", verifier),
        new("resource", Resource),
    ];

    private static KeyValuePair<string, string>[] RefreshForm(string refreshToken) =>
    [
        new("grant_type", "refresh_token"),
        new("refresh_token", refreshToken),
        new("client_id", ClientId),
        new("resource", Resource),
    ];

    private static async Task<JsonElement> TokenRequest(HttpClient client, IEnumerable<KeyValuePair<string, string>> form)
    {
        // The last client_id wins, so a test can override the one a form helper set.
        var fields = form.GroupBy(pair => pair.Key).Select(group => group.Last());
        using var response = await client.PostAsync("/api/oauth/token", new FormUrlEncodedContent(fields));
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<HttpStatusCode> ProbeStatus(WebApplicationFactory<Program> factory, string path, string token)
    {
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.GetAsync(path);
        return response.StatusCode;
    }

    private static (string Verifier, string Challenge) Pkce()
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        return (verifier, Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))));
    }

    private static Dictionary<string, string?> Query(string url) =>
        QueryHelpers.ParseQuery(new Uri(url).Query).ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToString());

    private static async Task<JsonDocument> JsonOf(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        using (response)
        {
            return await AdminApiFixture.ReadDataAsync<JsonElement>(response);
        }
    }

    /// <summary>Two protected resources that answer with the user id of a valid access token, or challenge.</summary>
    private sealed class Probes : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (!context.Request.Path.StartsWithSegments(ProbePath) && !context.Request.Path.StartsWithSegments(OtherProbePath))
                {
                    await nextMiddleware();
                    return;
                }

                var result = await context.AuthenticateAsync(OAuthServer.AccessTokenScheme);
                if (!result.Succeeded)
                {
                    await context.ChallengeAsync(OAuthServer.AccessTokenScheme);
                    return;
                }

                await context.Response.WriteAsync(result.Principal!.FindFirst("sub")!.Value);
            });
            next(app);
        };
    }

    private sealed class FakeClients : IOAuthClientDirectory
    {
        public Task<OAuthClient> Find(string? clientId, CancellationToken ct) => clientId == ClientId
            ? Task.FromResult(new OAuthClient(ClientId, "Test Assistant", "https://assistant.example",
                ["http://127.0.0.1/callback", "http://localhost/callback"]))
            : throw new OAuthClientException("Unknown client.");
    }
}
