using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Playdex.ContractTests;

/// <summary>
/// Contract-verification tests for the auth flow (login, token validation,
/// protected endpoint access).
///
/// Skips when <c>PLAYDEX_CONTRACT_DATABASE_URL</c> is not configured.
/// </summary>
public sealed class AuthContractTests : ContractTestBase
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
    };

    public AuthContractTests(ContractFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task Login_WithValidCredentials_Returns200AndTokenPair()
    {
        if (!Fixture.IsAvailable)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { email = Fixture.TestUserEmail, password = Fixture.TestUserPassword },
            JsonOptions);

        var bunTask = BunClient.PostAsync("/api/login", new ByteArrayContent(payload)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        });
        var dotnetTask = DotnetClient.PostAsync("/api/login", new ByteArrayContent(payload)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        });

        await Task.WhenAll(bunTask, dotnetTask);
        var bun = await bunTask;
        var dotnet = await dotnetTask;

        // Both must succeed (200 OK)
        Assert.Equal(HttpStatusCode.OK, bun.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dotnet.StatusCode);

        // Both responses must use the same envelope shape — extract tokens
        // for use in downstream tests, then re-insert a placeholder so the
        // body comparison still catches structural differences.
        var bunDoc = JsonDocument.Parse(await bun.Content.ReadAsStringAsync());
        var dotnetDoc = JsonDocument.Parse(await dotnet.Content.ReadAsStringAsync());

        // Same structural fields (userId, email, isAdmin, accessToken, refreshToken)
        // with volatile token values normalised.
        var bunBody = NormalizeResponseBody(await bun.Content.ReadAsStringAsync());
        var dotnetBody = NormalizeResponseBody(await dotnet.Content.ReadAsStringAsync());
        Assert.Equal(bunBody, dotnetBody);
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        if (!Fixture.IsAvailable)
            return;

        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { email = "wrong@example.com", password = "wrong-password" },
            JsonOptions);

        var bunTask = BunClient.PostAsync("/api/login", new ByteArrayContent(payload)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        });
        var dotnetTask = DotnetClient.PostAsync("/api/login", new ByteArrayContent(payload)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        });

        await Task.WhenAll(bunTask, dotnetTask);
        var bun = await bunTask;
        var dotnet = await dotnetTask;

        // Both must return 401 Unauthorized
        Assert.Equal(HttpStatusCode.Unauthorized, bun.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, dotnet.StatusCode);

        // Both error bodies must be structurally identical
        var bunBody = await bun.Content.ReadAsStringAsync();
        var dotnetBody = await dotnet.Content.ReadAsStringAsync();
        Assert.Equal(bunBody, dotnetBody);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsUserInfo()
    {
        if (!Fixture.IsAvailable)
            return;

        // First, log in to obtain tokens from both APIs.
        var payload = JsonSerializer.SerializeToUtf8Bytes(
            new { email = Fixture.TestUserEmail, password = Fixture.TestUserPassword },
            JsonOptions);

        var bunLogin = await BunClient.PostAsync("/api/login", new ByteArrayContent(payload)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        });
        var dotnetLogin = await DotnetClient.PostAsync("/api/login", new ByteArrayContent(payload)
        {
            Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
        });

        bunLogin.EnsureSuccessStatusCode();
        dotnetLogin.EnsureSuccessStatusCode();

        var bunDoc = JsonDocument.Parse(await bunLogin.Content.ReadAsStringAsync());
        var dotnetDoc = JsonDocument.Parse(await dotnetLogin.Content.ReadAsStringAsync());

        var bunAccessToken = bunDoc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;
        var dotnetAccessToken = dotnetDoc.RootElement.GetProperty("data").GetProperty("accessToken").GetString()!;

        // Now request /api/me with the token on both sides.
        var bunRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        bunRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bunAccessToken);

        var dotnetRequest = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        dotnetRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dotnetAccessToken);

        var bunTask = BunClient.SendAsync(bunRequest);
        var dotnetTask = DotnetClient.SendAsync(dotnetRequest);
        await Task.WhenAll(bunTask, dotnetTask);
        var bun = await bunTask;
        var dotnet = await dotnetTask;

        Assert.Equal(HttpStatusCode.OK, bun.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dotnet.StatusCode);

        // Response bodies should be identical (no volatile token fields in /me)
        var bunBody = await bun.Content.ReadAsStringAsync();
        var dotnetBody = await dotnet.Content.ReadAsStringAsync();
        Assert.Equal(bunBody, dotnetBody);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises token values in a login response so the body can be compared
    /// structurally between the two API implementations.
    /// </summary>
    private static string NormalizeResponseBody(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false });

        WriteNormalized(doc.RootElement, writer, "$");
        writer.Flush();
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteNormalized(JsonElement element, Utf8JsonWriter writer, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var prop in element.EnumerateObject())
                {
                    var childPath = $"{path}.{prop.Name}";
                    if (childPath is "$.data.accessToken" or "$.data.refreshToken")
                    {
                        writer.WriteString(prop.Name, "__VOLATILE__");
                    }
                    else
                    {
                        writer.WritePropertyName(prop.Name);
                        WriteNormalized(prop.Value, writer, childPath);
                    }
                }
                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                var idx = 0;
                foreach (var item in element.EnumerateArray())
                {
                    WriteNormalized(item, writer, $"{path}[{idx}]");
                    idx++;
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText());
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
        }
    }

}
