using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Infrastructure.Auth.OAuth;
using Api.Infrastructure.Common;
using Api.Modules.Manual.Mcp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>/api/mcp end to end: an MCP client with a Playdex app key, the draft card's tools and its token.</summary>
[Collection(AdminApiCollectionDefinition.Name)]
public sealed class McpDraftIntegrationTests(AdminApiFixture fixture)
{
    [Fact]
    public async Task OnlyDraftSessionsIsOfferedToTheModel()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        await using var mcp = await Connect(AdminApiFixture.UserEmail);

        var tools = (await mcp.ListToolsAsync()).ToDictionary(tool => tool.Name, tool => tool.ProtocolTool);
        Assert.Equal(["accept_drafts", "delete_draft", "draft_sessions", "search_games", "update_draft"], tools.Keys.Order());
        var draft = tools["draft_sessions"].Meta!["ui"]!;
        Assert.Equal(DraftCard.Uri, draft["resourceUri"]!.GetValue<string>());
        Assert.Equal(["model"], Visibility(tools["draft_sessions"]));
        foreach (var name in tools.Keys.Where(name => name != "draft_sessions"))
        {
            Assert.Equal(["app"], Visibility(tools[name]));
        }
    }

    [Fact]
    public async Task CardResourceIsAnMcpAppWithCoverOrigins()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        await using var mcp = await Connect(AdminApiFixture.UserEmail);

        var contents = Assert.IsType<TextResourceContents>(Assert.Single((await mcp.ReadResourceAsync(DraftCard.Uri)).Contents));
        Assert.Equal(DraftCard.MimeType, contents.MimeType);
        Assert.StartsWith("<!doctype html>", contents.Text);
        var domains = contents.Meta!["ui"]!["csp"]!["resourceDomains"]!.AsArray().Select(node => node!.GetValue<string>());
        Assert.Contains("https://images.igdb.com", domains);
    }

    [Fact]
    public async Task DraftedSessionIsSavedOnlyWhenAcceptedWithTheBatchToken()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        var title = $"Mcp Probe {Guid.NewGuid():N}";
        long gameId;
        await using (var db = fixture.CreateDb())
        {
            var game = new Game { Title = title, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            gameId = game.Id;
        }

        try
        {
            await using var mcp = await Connect(AdminApiFixture.UserEmail);
            var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var drafted = await mcp.CallToolAsync("draft_sessions", new Dictionary<string, object?>
            {
                ["sessions"] = new[] { new { title, date = yesterday, durationMinutes = 90, platform = "PC" } },
                ["timezone"] = "UTC",
            });

            Assert.NotEqual(true, drafted.IsError);
            var batch = drafted.StructuredContent!.Value.GetProperty("batch");
            var batchId = batch.GetProperty("id").GetGuid();
            var row = Assert.Single(batch.GetProperty("rows").EnumerateArray().ToArray());
            Assert.Equal(GameId.Encode(gameId), row.GetProperty("selection").GetProperty("gameId").GetString());
            Assert.Equal("pc", row.GetProperty("platformKey").GetString());
            Assert.True(row.GetProperty("ready").GetBoolean());
            var token = drafted.Meta!["confirmToken"]!.GetValue<string>();
            // The token rides in _meta for the card; the text the model reads does not carry it.
            Assert.DoesNotContain(token, Text(drafted));
            Assert.DoesNotContain(token, drafted.StructuredContent!.Value.GetRawText());
            Assert.Equal(0, await SessionsOf(gameId));

            var accept = new Dictionary<string, object?>
            {
                ["batchId"] = batchId,
                ["rows"] = new[] { new { id = row.GetProperty("id").GetGuid(), revision = row.GetProperty("revision").GetInt32() } },
            };
            Assert.True((await mcp.CallToolAsync("accept_drafts", accept)).IsError);
            Assert.True((await mcp.CallToolAsync("accept_drafts", new Dictionary<string, object?>(accept) { ["confirmToken"] = "AAAA" })).IsError);
            await using (var admin = await Connect(AdminApiFixture.AdminEmail))
            {
                // Another user's batch is not theirs to accept, even with its token.
                var foreign = await admin.CallToolAsync("accept_drafts", new Dictionary<string, object?>(accept) { ["confirmToken"] = token });
                Assert.True(foreign.IsError);
            }
            Assert.Equal(0, await SessionsOf(gameId));

            var accepted = await mcp.CallToolAsync("accept_drafts", new Dictionary<string, object?>(accept) { ["confirmToken"] = token });

            Assert.NotEqual(true, accepted.IsError);
            var result = Assert.Single(accepted.StructuredContent!.Value.GetProperty("results").EnumerateArray().ToArray());
            Assert.Equal(JsonValueKind.Number, result.GetProperty("sessionId").ValueKind);
            Assert.Equal(1, await SessionsOf(gameId));

        }
        finally
        {
            await using var db = fixture.CreateDb();
            await db.PlaySessions.Where(session => session.GameId == gameId).ExecuteDeleteAsync();
            await db.Games.Where(game => game.Id == gameId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task CardToolsNeedTheBatchTokenOfTheirOwnUser()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        await using var mcp = await Connect(AdminApiFixture.UserEmail);
        var drafted = await mcp.CallToolAsync("draft_sessions", new Dictionary<string, object?>
        {
            ["sessions"] = new[] { new { title = $"Unmatched {Guid.NewGuid():N}", date = "2026-01-01", durationMinutes = 30 } },
        });
        var batch = drafted.StructuredContent!.Value.GetProperty("batch");
        var row = batch.GetProperty("rows")[0];
        var token = drafted.Meta!["confirmToken"]!.GetValue<string>();
        var target = new Dictionary<string, object?>
        {
            ["batchId"] = batch.GetProperty("id").GetGuid(),
            ["rowId"] = row.GetProperty("id").GetGuid(),
            ["revision"] = row.GetProperty("revision").GetInt32(),
        };
        var update = new Dictionary<string, object?>(target) { ["title"] = "Renamed", ["platform"] = "pc" };

        Assert.True((await mcp.CallToolAsync("update_draft", new Dictionary<string, object?>(update) { ["confirmToken"] = "AAAA" })).IsError);
        Assert.True((await mcp.CallToolAsync("delete_draft", new Dictionary<string, object?>(target) { ["confirmToken"] = "AAAA" })).IsError);
        await using (var admin = await Connect(AdminApiFixture.AdminEmail))
        {
            Assert.True((await admin.CallToolAsync("update_draft", new Dictionary<string, object?>(update) { ["confirmToken"] = token })).IsError);
            Assert.True((await admin.CallToolAsync("delete_draft", new Dictionary<string, object?>(target) { ["confirmToken"] = token })).IsError);
        }

        var renamed = await mcp.CallToolAsync("update_draft", new Dictionary<string, object?>(update) { ["confirmToken"] = token });
        Assert.NotEqual(true, renamed.IsError);
        var updated = renamed.StructuredContent!.Value.GetProperty("row");
        Assert.Equal("Renamed", updated.GetProperty("title").GetString());

        // The revision moved on: a card holding the old one is told to reload rather than overwrite.
        var stale = await mcp.CallToolAsync("delete_draft", new Dictionary<string, object?>(target) { ["confirmToken"] = token });
        Assert.True(stale.IsError);
        Assert.Contains("changed", Text(stale));
        var deleted = await mcp.CallToolAsync("delete_draft", new Dictionary<string, object?>(target)
        {
            ["confirmToken"] = token,
            ["revision"] = updated.GetProperty("revision").GetInt32(),
        });
        Assert.NotEqual(true, deleted.IsError);
    }

    [Fact]
    public async Task McpIsAnOAuthProtectedResource()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = fixture.CreateClient();
        using var challenge = await client.PostAsync("/api/mcp", Initialize());
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource/api/mcp\"",
            challenge.Headers.WwwAuthenticate.ToString());
        using var metadata = JsonDocument.Parse(await client.GetStringAsync("/.well-known/oauth-protected-resource/api/mcp"));
        Assert.Equal("http://localhost/api/mcp", metadata.RootElement.GetProperty("resource").GetString());
        Assert.Equal(["drafts"], metadata.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()));

        Guid grantId;
        await using (var db = fixture.CreateDb())
        {
            var version = await db.Users.Where(u => u.Id == fixture.UserId).Select(u => u.TokenVersion).SingleAsync();
            var grant = new OAuthGrant
            {
                Id = Guid.NewGuid(),
                UserId = fixture.UserId,
                ClientId = "https://assistant.example/client.json",
                ClientName = "Assistant",
                Scope = "drafts",
                Resource = "http://localhost/api/mcp",
                TokenVersion = version,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.OAuthGrants.Add(grant);
            await db.SaveChangesAsync();
            grantId = grant.Id;
        }

        var tokens = fixture.Factory.Services.GetRequiredService<OAuthTokens>();
        var accessToken = tokens.IssueAccessToken("http://localhost", "http://localhost/api/mcp", fixture.UserId, grantId,
            "https://assistant.example/client.json", "drafts", DateTime.UtcNow);
        await using (var mcp = await ConnectWith(accessToken))
        {
            Assert.Contains(await mcp.ListToolsAsync(), tool => tool.Name == "draft_sessions");
        }

        // A token for another resource, or with a scope MCP does not offer, is refused.
        var elsewhere = tokens.IssueAccessToken("http://localhost", "http://localhost/api/other", fixture.UserId, grantId,
            "https://assistant.example/client.json", "drafts", DateTime.UtcNow);
        var otherScope = tokens.IssueAccessToken("http://localhost", "http://localhost/api/mcp", fixture.UserId, grantId,
            "https://assistant.example/client.json", "sessions", DateTime.UtcNow);
        foreach (var refused in new[] { elsewhere, otherScope })
        {
            using var http = fixture.CreateClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refused);
            using var response = await http.PostAsync("/api/mcp", Initialize());
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task InvalidRowsAreReportedToTheModel()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        await using var mcp = await Connect(AdminApiFixture.UserEmail);

        var result = await mcp.CallToolAsync("draft_sessions", new Dictionary<string, object?>
        {
            ["sessions"] = new[] { new { title = "  ", date = "2026-01-01", durationMinutes = 30 } },
        });

        Assert.True(result.IsError);
        Assert.Contains("game title", Text(result));
    }

    [Fact]
    public async Task McpNeedsAnAppKey()
    {
        if (fixture.Factory is null)
        {
            return;
        }

        using var client = fixture.CreateClient();
        using var anonymous = await client.PostAsync("/api/mcp", Initialize());
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // A web session's JWT is not an app key.
        using var signedIn = await fixture.LoginAsync(AdminApiFixture.UserEmail);
        using var jwt = await signedIn.PostAsync("/api/mcp", Initialize());
        Assert.Equal(HttpStatusCode.Unauthorized, jwt.StatusCode);
    }

    private async Task<McpClient> Connect(string email)
    {
        using var signedIn = await fixture.LoginAsync(email);
        var key = (await AdminApiFixture.ReadDataAsync<JsonElement>(await signedIn.PostAsync("/api/key", null)))
            .GetProperty("apiKey").GetString()!;
        return await ConnectWith(key);
    }

    private async Task<McpClient> ConnectWith(string bearer)
    {
        var http = fixture.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/api/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private async Task<int> SessionsOf(long gameId)
    {
        await using var db = fixture.CreateDb();
        return await db.PlaySessions.CountAsync(session => session.GameId == gameId);
    }

    private static string[] Visibility(Tool tool) =>
        tool.Meta!["ui"]!["visibility"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static StringContent Initialize() => new("""
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """, Encoding.UTF8, "application/json");
}
