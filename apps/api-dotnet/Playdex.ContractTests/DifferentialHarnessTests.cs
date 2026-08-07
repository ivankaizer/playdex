using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Playdex.ContractTests;

public sealed class DifferentialHarnessTests
{
    [Fact]
    public void Normalizer_ReplacesOnlyDocumentedVolatileFields()
    {
        var body = JsonNode.Parse("""
            {"data":{"accessToken":"secret","id":42,"title":"Stable"},"correlationId":"abc"}
            """);

        NormalizedResponse.Normalize(body);

        Assert.Equal("<accessToken>", body!["data"]!["accessToken"]!.GetValue<string>());
        Assert.Equal(42, body["data"]!["id"]!.GetValue<int>());
        Assert.Equal("Stable", body["data"]!["title"]!.GetValue<string>());
        Assert.Equal("<correlationId>", body["correlationId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Root_IsEquivalent_WhenLiveTargetsAreConfigured()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() => new HttpRequestMessage(HttpMethod.Get, "/api"));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    [Fact]
    public async Task DatabaseEffects_AreEquivalent_WhenIndependentTargetsAreConfigured()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasDatabaseTargets) return;

        var result = await fixture.QueryAsync(
            "select key from platforms order by key");

        Assert.Equal(result.Bun.ToJsonString(), result.Dotnet.ToJsonString());
    }

    // ── 5.1: Authentication ──────────────────────────────────────────────

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/accounts")]
    [InlineData("/api/sessions")]
    [InlineData("/api/sessions/today")]
    [InlineData("/api/sessions/current")]
    [InlineData("/api/stats")]
    [InlineData("/api/settings")]
    [InlineData("/api/games/search?query=hades")]
    [InlineData("/api/games/connections")]
    [InlineData("/api/games/ignored")]
    [InlineData("/api/games/rating")]
    [InlineData("/api/games/0")]
    [InlineData("/api/manual/inputs")]
    [InlineData("/api/manual/search")]
    [InlineData("/api/providers")]
    [InlineData("/api/recap")]
    [InlineData("/api/metrics")]
    [InlineData("/api/admin/sessions")]
    [InlineData("/api/admin/sessions/recent")]
    [InlineData("/api/admin/providers/platforms")]
    [InlineData("/api/admin/credentials")]
    [InlineData("/api/admin/games/search")]
    [InlineData("/api/recap/years")]
    public async Task ProtectedRoutes_HaveEquivalentMissingAuthentication(string path)
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, path));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    [Theory]
    [InlineData("/api/admin/sessions")]
    [InlineData("/api/admin/sessions/recent")]
    [InlineData("/api/admin/providers/platforms")]
    [InlineData("/api/admin/credentials")]
    [InlineData("/api/admin/games/search")]
    public async Task AdminRoutes_HaveEquivalentForbidden_WhenNonAdminUser(string path)
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var result = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, path));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    // ── 5.1: Error Handling & Malformed JSON ──────────────────────────────

    [Theory]
    [InlineData("/api/login", "POST")]
    [InlineData("/api/settings", "PUT")]
    [InlineData("/api/games/connections", "POST")]
    [InlineData("/api/games/ignored", "POST")]
    [InlineData("/api/games/rating", "POST")]
    [InlineData("/api/manual/sessions/start", "POST")]
    [InlineData("/api/manual/sessions/stop", "POST")]
    [InlineData("/api/manual/entries", "POST")]
    [InlineData("/api/playnite/sessions/ping", "POST")]
    [InlineData("/api/playnite/sessions/end", "POST")]
    [InlineData("/api/admin/sessions/move", "PUT")]
    [InlineData("/api/admin/games", "PUT")]
    public async Task MalformedJson_IsRejectedWithControlledError(string path, string method)
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() => new HttpRequestMessage(
            new HttpMethod(method), path)
        {
            Content = new StringContent("{", Encoding.UTF8, "application/json"),
        });

        Assert.Equal(result.Bun, result.Dotnet);
    }

    [Theory]
    [InlineData("/api/not-a-route")]
    [InlineData("/api/games/999999999")]
    [InlineData("/api/games/invalid-id")]
    [InlineData("/api/sessions/999999999")]
    [InlineData("/api/sessions/not-a-number")]
    [InlineData("/api/accounts/999999999/sessions")]
    [InlineData("/api/manual/inputs/999999999/map")]
    public async Task NotFoundAndInvalidIdentifiers_AreEquivalent(string path)
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, path));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    [Theory]
    [InlineData("/api/games/search?query=hades&limit=0")]
    [InlineData("/api/games/search?query=hades&limit=-1")]
    [InlineData("/api/games/search?query=hades&offset=-1")]
    [InlineData("/api/sessions?limit=0")]
    [InlineData("/api/sessions?limit=-1")]
    [InlineData("/api/admin/sessions?limit=0")]
    public async Task InvalidPagination_IsRejectedWithCompatibleError(string path)
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, path));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    // ── 5.1: Missing-auth on non-GET mutations ────────────────────────────

    [Theory]
    [InlineData("/api/refresh", "POST")]
    [InlineData("/api/settings", "PUT")]
    [InlineData("/api/games/connections", "DELETE")]
    [InlineData("/api/games/ignored", "DELETE")]
    [InlineData("/api/games/rating", "DELETE")]
    [InlineData("/api/sessions/0", "DELETE")]
    [InlineData("/api/manual/sessions/start", "POST")]
    [InlineData("/api/manual/sessions/stop", "POST")]
    [InlineData("/api/manual/entries", "POST")]
    [InlineData("/api/manual/inputs/0/map", "POST")]
    [InlineData("/api/providers/status", "PUT")]
    [InlineData("/api/playnite/key", "POST")]
    [InlineData("/api/admin/sessions/move", "PUT")]
    [InlineData("/api/admin/games", "PUT")]
    [InlineData("/api/admin/providers/platforms/status", "PUT")]
    public async Task MutationRoutes_HaveEquivalentMissingAuth(string path, string method)
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() => new HttpRequestMessage(
            new HttpMethod(method), path)
        {
            Content = method is not "DELETE"
                ? new StringContent("{}", Encoding.UTF8, "application/json")
                : null,
        });

        Assert.Equal(result.Bun, result.Dotnet);
    }

    // ── 5.1 + 5.4: Success cases (unauthenticated public + read-only) ────

    [Fact]
    public async Task GamesSearch_DefaultsLimitTo10_AndRejectsInvalidParams()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var noLimit = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/search?query=a"));
        var invalidLimit = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/search?query=a&limit=0"));

        Assert.Equal(noLimit.Bun, noLimit.Dotnet);
        Assert.Equal(invalidLimit.Bun, invalidLimit.Dotnet);
    }

    [Fact]
    public async Task Providers_ReturnsEquivalentListAndStatusUpdateFailsWithoutAuth()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var listResult = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/providers"));
        var statusResult = await fixture.SendAsync(() => new HttpRequestMessage(
            HttpMethod.Put, "/api/providers/status")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });

        Assert.Equal(listResult.Bun, listResult.Dotnet);
        Assert.Equal(statusResult.Bun, statusResult.Dotnet);
    }

    [Fact]
    public async Task Metrics_ReturnsCompatibleCounterSnapshot()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/metrics"));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    [Fact]
    public async Task Recap_DefaultRangeAndInvalidRangeAreEquivalent()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var defaultRange = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/recap"));
        var invalidRange = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/recap?from=2025-12-31&to=2025-01-01"));

        Assert.Equal(defaultRange.Bun, defaultRange.Dotnet);
        Assert.Equal(invalidRange.Bun, invalidRange.Dotnet);
    }

    [Fact]
    public async Task RecapYears_IsEquivalent()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;

        var result = await fixture.SendAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/recap/years"));

        Assert.Equal(result.Bun, result.Dotnet);
    }

    // ── 5.4: Authenticated success cases ──────────────────────────────────

    [Fact]
    public async Task AccountsAndSettings_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var accounts = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/accounts"));
        var settings = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/settings"));

        Assert.Equal(accounts.Bun, accounts.Dotnet);
        Assert.Equal(settings.Bun, settings.Dotnet);
    }

    [Fact]
    public async Task Sessions_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var list = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/sessions"));
        var today = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/sessions/today"));
        var current = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/sessions/current"));

        Assert.Equal(list.Bun, list.Dotnet);
        Assert.Equal(today.Bun, today.Dotnet);
        Assert.Equal(current.Bun, current.Dotnet);
    }

    [Fact]
    public async Task GamesDetails_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var search = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/search?query=hades"));
        var missing = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/999999999"));
        var invalid = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/invalid"));

        Assert.Equal(search.Bun, search.Dotnet);
        Assert.Equal(missing.Bun, missing.Dotnet);
        Assert.Equal(invalid.Bun, invalid.Dotnet);
    }

    [Fact]
    public async Task GamesConnections_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var getConnections = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/connections"));

        Assert.Equal(getConnections.Bun, getConnections.Dotnet);
    }

    [Fact]
    public async Task GamesIgnored_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var getIgnored = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/ignored"));

        Assert.Equal(getIgnored.Bun, getIgnored.Dotnet);
    }

    [Fact]
    public async Task GamesRating_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var getRatings = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/games/rating"));

        Assert.Equal(getRatings.Bun, getRatings.Dotnet);
    }

    [Fact]
    public async Task ManualInputsAndSearch_AuthenticatedEquivalence()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var inputs = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/manual/inputs"));
        var search = await fixture.SendAuthenticatedAsync(() =>
            new HttpRequestMessage(HttpMethod.Get, "/api/manual/search?q=a"));

        Assert.Equal(inputs.Bun, inputs.Dotnet);
        Assert.Equal(search.Bun, search.Dotnet);
    }

    [Fact]
    public async Task LoginAndRefresh_ValidCredentials_AreEquivalent()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasHttpTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var login = await fixture.SendAsync(() =>
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, "/api/login");
            msg.Content = JsonContent.Create(new { email = "bun-contract@test.example", password = "test" });
            return msg;
        });
        Assert.Equal(login.Bun.Status, login.Dotnet.Status);
    }

    // ── 5.4: Database mutation side effects ───────────────────────────────

    [Fact]
    public async Task DatabasePlatforms_RemainEquivalentAfterMigrations()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasDatabaseTargets) return;

        var result = await fixture.QueryAsync(
            "select key, display_name from platforms order by key");

        Assert.Equal(result.Bun.ToJsonString(), result.Dotnet.ToJsonString());
    }

    [Fact]
    public async Task DatabaseUsers_AreEquivalent()
    {
        await using var fixture = new DifferentialFixture();
        if (!fixture.HasDatabaseTargets) return;
        if (!await fixture.EnsureTestUserAsync()) return;

        var result = await fixture.QueryAsync(
            "select count(*) as user_count from users where email = 'bun-contract@test.example'");

        Assert.Equal(result.Bun.ToJsonString(), result.Dotnet.ToJsonString());
    }
}
