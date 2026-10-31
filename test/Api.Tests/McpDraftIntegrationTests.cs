using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Api.Infrastructure.Common;
using Api.Modules.Manual.Mcp;
using Microsoft.EntityFrameworkCore;
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
