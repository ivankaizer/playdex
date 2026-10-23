using Api.Modules.Notifications;
using Xunit;

namespace Api.Tests;

public sealed class CoverUrlPolicyTests
{
    private static readonly CoverUrlPolicy _policy = new([
        "https://dash.playdex.test",
        "http://localhost:3000/",
        "https://api-cdn.wemod.com",
        null,
        "",
        "not a url",
    ]);

    [Theory]
    [InlineData("https://api-cdn.wemod.com/steam_apps/1245620/hero_capsule.webp")]
    [InlineData("https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/1245620/header.jpg")]
    [InlineData("https://cdn.cloudflare.steamstatic.com/steam/apps/570/library_600x900_2x.jpg")]
    [InlineData("https://image.api.playstation.com/vulcan/ap/rnd/202402/2214/cover.jpg?w=1920&thumb=false")]
    [InlineData("https://store-images.s-microsoft.com/image/apps.12345.poster")]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg")]
    [InlineData("https://dash.playdex.test/api/posters/steam/1245620")]
    [InlineData("http://localhost:3000/api/posters/steam/1245620")]
    public void CoversFromPlaydexSourcesAreAllowed(string url) => Assert.True(_policy.IsAllowed(url));

    [Theory]
    [InlineData("http://shared.fastly.steamstatic.com/header.jpg")]       // known host, but not https
    [InlineData("https://images.igdb.com:8443/cover.jpg")]               // known host on another port
    [InlineData("https://steamstatic.com.evil.test/header.jpg")]         // lookalike suffix
    [InlineData("https://evilsteamstatic.com/header.jpg")]               // lookalike prefix
    [InlineData("https://user:pass@images.igdb.com/cover.jpg")]          // credentials in the URL
    [InlineData("http://localhost:8080/api/posters/steam/1")]            // configured host, other port
    [InlineData("http://dash.playdex.test/api/posters/steam/1")]         // configured origin is https only
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/api/posters/steam/1")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsRefused(string? url) => Assert.False(_policy.IsAllowed(url));

    [Fact]
    public void WithoutConfiguredOriginsOnlyKnownHostsAreAllowed()
    {
        var policy = new CoverUrlPolicy([]);

        Assert.True(policy.IsAllowed("https://images.igdb.com/igdb/image/upload/t_cover_big/co1abc.jpg"));
        Assert.False(policy.IsAllowed("https://dash.playdex.test/api/posters/steam/1245620"));
    }
}
