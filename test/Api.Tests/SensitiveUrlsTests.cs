using Api.Observability;
using Sentry;
using Xunit;

namespace Api.Tests;

public sealed class SensitiveUrlsTests
{
    [Theory]
    [InlineData(
        "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=ABCDEF0123&steamids=7656",
        "https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key=[redacted]&steamids=7656")]
    [InlineData("https://example.com/a?steamids=1&KEY=x#frag", "https://example.com/a?steamids=1&KEY=[redacted]#frag")]
    [InlineData("https://example.com/a?keyboard=1", "https://example.com/a?keyboard=1")]
    public void RedactsSecretQueryParameters(string url, string expected) =>
        Assert.Equal(expected, SensitiveUrls.Redact(url));

    [Fact]
    public void RedactsHttpBreadcrumbData()
    {
        var breadcrumb = new Breadcrumb(
            "",
            "http",
            new Dictionary<string, string> { ["url"] = "https://api.steampowered.com/x/?key=SECRET", ["method"] = "GET" },
            "http.client");

        var redacted = SensitiveUrls.Redact(breadcrumb);

        Assert.Equal("https://api.steampowered.com/x/?key=[redacted]", redacted.Data!["url"]);
        Assert.Equal("GET", redacted.Data["method"]);
        Assert.Equal("http.client", redacted.Category);
    }
}
