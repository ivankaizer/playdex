using System.Net;
using System.Text;
using Api.Infrastructure.Auth.OAuth;
using Xunit;

namespace Api.Tests;

public sealed class OAuthClientsTests
{
    private const string ClientId = "https://claude.ai/oauth/claude-code-client-metadata";

    [Theory]
    [InlineData("https://claude.ai/oauth/claude-code-client-metadata", true)]
    [InlineData("http://claude.ai/oauth/client.json", false)]
    [InlineData("https://claude.ai", false)]
    [InlineData("https://claude.ai/", false)]
    [InlineData("https://claude.ai/client.json#x", false)]
    [InlineData("https://user@claude.ai/client.json", false)]
    [InlineData("claude-code", false)]
    [InlineData(null, false)]
    public void ClientIdsAreHttpsDocumentUrls(string? clientId, bool valid) =>
        Assert.Equal(valid, ClientMetadataDocuments.IsDocumentUrl(clientId, out _));

    [Fact]
    public void DocumentMustNameItsOwnUrlAndRedirects()
    {
        var client = ClientMetadataDocuments.Parse(ClientId, Json($$"""
            {"client_id":"{{ClientId}}","client_name":" Claude Code ","redirect_uris":["http://localhost/callback","http://127.0.0.1/callback"],"token_endpoint_auth_method":"none"}
            """));
        Assert.Equal("Claude Code", client.Name);
        Assert.True(client.IsLoopbackOnly);

        Assert.Throws<OAuthClientException>(() => ClientMetadataDocuments.Parse(ClientId,
            Json("""{"client_id":"https://evil.example/client.json","redirect_uris":["https://evil.example/cb"]}""")));
        Assert.Throws<OAuthClientException>(() => ClientMetadataDocuments.Parse(ClientId,
            Json($$"""{"client_id":"{{ClientId}}","redirect_uris":[]}""")));
        Assert.Throws<OAuthClientException>(() => ClientMetadataDocuments.Parse(ClientId,
            Json($$"""{"client_id":"{{ClientId}}","redirect_uris":["https://a.example/cb"],"token_endpoint_auth_method":"private_key_jwt"}""")));
        Assert.Throws<OAuthClientException>(() => ClientMetadataDocuments.Parse(ClientId, Json("not json")));
    }

    [Fact]
    public void NameFallsBackToTheClientHost()
    {
        var client = ClientMetadataDocuments.Parse(ClientId, Json($$"""{"client_id":"{{ClientId}}","redirect_uris":["https://claude.ai/api/mcp/auth_callback"]}"""));
        Assert.Equal("claude.ai", client.Name);
        Assert.False(client.IsLoopbackOnly);
    }

    [Theory]
    [InlineData("https://claude.ai/api/mcp/auth_callback", true)]
    [InlineData("https://claude.ai/api/mcp/auth_callback/", false)]
    [InlineData("https://claude.ai/api/mcp/auth_callback?x=1", false)]
    [InlineData("http://localhost:3118/callback", true)]
    [InlineData("http://127.0.0.1:50000/callback", true)]
    [InlineData("http://localhost/callback", true)]
    [InlineData("http://localhost:3118/other", false)]
    [InlineData("https://localhost:3118/callback", false)]
    [InlineData("http://localhost.evil.example:3118/callback", false)]
    [InlineData("http://127.0.0.1:50000/callback#frag", false)]
    public void RedirectsMatchExactlyOrAsLoopbackOnAnyPort(string redirect, bool allowed)
    {
        var client = new OAuthClient(ClientId, "Claude", null,
            ["https://claude.ai/api/mcp/auth_callback", "http://localhost/callback", "http://127.0.0.1/callback"]);
        Assert.Equal(allowed, client.AllowsRedirect(redirect));
    }

    [Theory]
    [InlineData("160.79.104.10", true)]
    [InlineData("2606:4700::1", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.5", false)]
    [InlineData("192.168.68.75", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    public void OnlyPublicAddressesAreFetched(string address, bool isPublic) =>
        Assert.Equal(isPublic, ClientMetadataDocuments.IsPublic(IPAddress.Parse(address)));

    [Theory]
    [InlineData("https://dash.playdex.dev/api/mcp", true)]
    [InlineData("https://DASH.playdex.dev/api/mcp/", true)]
    [InlineData("https://dash.playdex.dev/api/mcp?x=1", false)]
    [InlineData("https://dash.playdex.dev/api", false)]
    [InlineData("https://other.example/api/mcp", false)]
    public void ResourceMatchesTheMcpEndpoint(string requested, bool matches) =>
        Assert.Equal(matches, OAuthServer.IsResource(requested, "https://dash.playdex.dev/api/mcp"));

    private static byte[] Json(string json) => Encoding.UTF8.GetBytes(json);
}
