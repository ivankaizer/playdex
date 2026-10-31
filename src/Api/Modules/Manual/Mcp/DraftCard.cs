using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Api.Modules.Manual.Mcp;

/// <summary>
/// The card <c>draft_sessions</c> renders (an MCP Apps <c>ui://</c> resource): the drafted rows, editing, removing and
/// accepting them. It is styled only with the host's CSS variables, so it looks like the chat it sits in.
/// </summary>
[McpServerResourceType]
public sealed class DraftCard
{
    public const string Uri = "ui://playdex/drafts";
    public const string MimeType = "text/html;profile=mcp-app";

    // Where draft covers come from: Steam capsules (the WeMod CDN or Steam's own), PSN, Xbox and IGDB. The configured
    // poster origin is added at read time.
    private static readonly string[] _coverOrigins =
    [
        "https://api-cdn.wemod.com",
        "https://*.steamstatic.com",
        "https://image.api.playstation.com",
        "https://store-images.s-microsoft.com",
        "https://images-eds-ssl.xboxlive.com",
        "https://images.igdb.com",
    ];

    private static readonly Lazy<string> _html = new(() =>
    {
        using var stream = typeof(DraftCard).Assembly.GetManifestResourceStream("Api.Modules.Manual.Mcp.drafts.html")
            ?? throw new InvalidOperationException("The draft card resource is missing from the build.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    [McpServerResource(UriTemplate = Uri, Name = "playdex-drafts", Title = "Playdex drafts", MimeType = MimeType)]
    [Description("Review card for drafted play sessions.")]
    public static ResourceContents Read(IConfiguration configuration) => new TextResourceContents
    {
        Uri = Uri,
        MimeType = MimeType,
        Text = _html.Value,
        Meta = new JsonObject
        {
            ["ui"] = new JsonObject
            {
                ["csp"] = new JsonObject { ["resourceDomains"] = new JsonArray([.. ResourceDomains(configuration["Posters:PublicBaseUrl"])]) },
                ["prefersBorder"] = true,
            },
            ["openai/widgetCSP"] = new JsonObject
            {
                ["connect_domains"] = new JsonArray(),
                ["resource_domains"] = new JsonArray([.. ResourceDomains(configuration["Posters:PublicBaseUrl"])]),
            },
        },
    };

    internal static IEnumerable<JsonNode?> ResourceDomains(string? posterBaseUrl)
    {
        var origins = _coverOrigins.ToList();
        if (System.Uri.TryCreate(posterBaseUrl?.Trim(), UriKind.Absolute, out var poster) && poster.Scheme is "https" or "http")
        {
            origins.Insert(0, poster.GetLeftPart(UriPartial.Authority));
        }

        return origins.Select(origin => (JsonNode?)JsonValue.Create(origin));
    }
}
