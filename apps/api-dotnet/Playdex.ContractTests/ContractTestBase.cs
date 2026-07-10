using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Xunit;

namespace Playdex.ContractTests;

/// <summary>
/// Base class for differential API contract tests.
///
/// Provides <see cref="BunClient"/> and <see cref="DotnetClient"/> pre-configured
/// <c>HttpClient</c> instances that target the running reference and candidate
/// APIs respectively.  The <c>AssertEquivalentResponse</c> helper compares two
/// HTTP responses and normalizes known volatile fields (tokens, correlation IDs,
/// date headers) before assertion so that only meaningful contract differences
/// cause test failures.
/// </summary>
public abstract class ContractTestBase : IClassFixture<ContractFixture>
{
    /// <summary>The fixture that manages seed data.</summary>
    protected ContractFixture Fixture { get; }

    /// <summary>HTTP client pointing at the Bun (reference) API.</summary>
    protected HttpClient BunClient { get; }

    /// <summary>HTTP client pointing at the .NET (candidate) API.</summary>
    protected HttpClient DotnetClient { get; }

    /// <summary>JSON serialiser options matching both APIs.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Property paths whose value is considered volatile and should be
    /// replaced with <c>"__VOLATILE__"</c> before comparison.</summary>
    private static readonly HashSet<string> VolatilePaths =
    [
        "$.data.accessToken",
        "$.data.refreshToken",
    ];

    /// <summary>HTTP response headers that may legitimately differ between two
    /// independent API instances and should be excluded from comparison.</summary>
    private static readonly HashSet<string> VolatileHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Date",
            "Content-Length",
            "Transfer-Encoding",
            "Server",
            "X-Correlation-ID",
            "X-Powered-By",
            "Set-Cookie",
            "Connection",
            "Keep-Alive",
        };

    protected ContractTestBase(ContractFixture fixture)
    {
        Fixture = fixture;

        var bunBase = Environment.GetEnvironmentVariable("BUN_API_BASE_URL")?.TrimEnd('/')
                      ?? "http://localhost:3000";
        var dotnetBase = Environment.GetEnvironmentVariable("DOTNET_API_BASE_URL")?.TrimEnd('/')
                         ?? "http://localhost:5000";

        BunClient = new HttpClient { BaseAddress = new Uri(bunBase + "/") };
        DotnetClient = new HttpClient { BaseAddress = new Uri(dotnetBase + "/") };
    }

    /// <summary>
    /// Sends the same <c>request</c> to both APIs and compares the responses.
    /// Fails with a descriptive message when responses diverge.
    /// </summary>
    protected async Task AssertEquivalentEndpoints(
        Func<HttpClient, Task<HttpResponseMessage>> request,
        string description)
    {
        var bunTask = request(BunClient);
        var dotnetTask = request(DotnetClient);

        await Task.WhenAll(bunTask, dotnetTask);

        var bunResponse = bunTask.Result;
        var dotnetResponse = dotnetTask.Result;

        AssertEquivalentResponse(bunResponse, dotnetResponse, description);
    }

    /// <summary>
    /// Asserts that two HTTP responses (status, relevant headers, and JSON body)
    /// are contract-equivalent.
    /// </summary>
    protected void AssertEquivalentResponse(
        HttpResponseMessage bunResponse,
        HttpResponseMessage dotnetResponse,
        string? description = null)
    {
        var prefix = description is not null ? $"[{description}] " : "";

        // ── Status code ────────────────────────────────────────────────
        Assert.Equal(bunResponse.StatusCode, dotnetResponse.StatusCode);

        // ── Content-Type ───────────────────────────────────────────────
        Assert.Equal(
            bunResponse.Content.Headers.ContentType?.ToString(),
            dotnetResponse.Content.Headers.ContentType?.ToString());

        // ── Remaining non-volatile headers ──────────────────────────────
        var bunHeaders = CollectNonVolatileHeaders(bunResponse);
        var dotnetHeaders = CollectNonVolatileHeaders(dotnetResponse);
        Assert.Equal(bunHeaders, dotnetHeaders);

        // ── Body ────────────────────────────────────────────────────────
        var bunBody = bunResponse.Content.ReadAsStringAsync().Result;
        var dotnetBody = dotnetResponse.Content.ReadAsStringAsync().Result;

        var normalizedBun = NormalizeVolatileFields(bunBody);
        var normalizedDotnet = NormalizeVolatileFields(dotnetBody);

        Assert.Equal(normalizedBun, normalizedDotnet);
    }

    // ---- Volatile-field normalisation ----------------------------------

    /// <summary>
    /// Parses a JSON response body and replaces known volatile property values
    /// (JWT tokens, timestamps, etc.) with deterministic placeholders so that
    /// responses from two different API instances can be compared.
    /// </summary>
    private static string NormalizeVolatileFields(string json)
    {
        using var doc = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

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
                    if (VolatilePaths.Contains(childPath))
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
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    WriteNormalized(item, writer, $"{path}[{index}]");
                    index++;
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

    // ---- Header helpers -------------------------------------------------

    private static string CollectNonVolatileHeaders(HttpResponseMessage response)
    {
        var entries = new List<string>();

        foreach (var (key, values) in response.Headers)
        {
            if (VolatileHeaders.Contains(key))
                continue;
            foreach (var value in values)
                entries.Add($"{key}: {value}");
        }

        foreach (var (key, values) in response.Content.Headers)
        {
            if (VolatileHeaders.Contains(key))
                continue;
            foreach (var value in values)
                entries.Add($"{key}: {value}");
        }

        entries.Sort(StringComparer.Ordinal);
        return string.Join("\n", entries);
    }
}
