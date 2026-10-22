using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Playdex.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class LauncherSummaryEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SummaryTakesTheApiKeyAndReturnsTheUsersPlayWithSteamIdentities()
    {
        if (!postgres.IsConfigured) return;

        ConfigureHost();
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/launcher/summary")).StatusCode);

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var steam = await db.Platforms.SingleAsync(platform => platform.Key == "steam");
        var user = new User { Email = $"launcher-summary-{suffix}@example.test", CreatedAt = now, UpdatedAt = now };
        var other = new User { Email = $"launcher-summary-other-{suffix}@example.test", CreatedAt = now, UpdatedAt = now };
        var game = new Game { Title = $"Launcher Game {suffix}", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, other, game);
        await db.SaveChangesAsync();
        var appId = $"9{Random.Shared.Next(10_000_000, 99_999_999)}";
        var identity = new GameExternalIdentity
        {
            GameId = game.Id, Type = GameExternalIdentityType.Steam, ExternalId = appId, Title = game.Title, CreatedAt = now, UpdatedAt = now,
        };
        var integration = new Integration
        {
            UserId = user.Id, Type = IntegrationType.Api, ApiKey = $"key-{suffix}", Active = true, CreatedAt = now, UpdatedAt = now,
        };
        db.AddRange(identity, integration);
        await db.SaveChangesAsync();
        PlaySession Session(long userId, DateTimeOffset startedAt, int minutes) => new()
        {
            // A session with no integration must be manual (play_sessions_manual_integration_check).
            GameId = game.Id, UserId = userId, PlatformId = steam.Id,
            CaptureMethod = userId == user.Id ? CaptureMethod.Api : CaptureMethod.Manual,
            IntegrationId = userId == user.Id ? integration.Id : null,
            StartedAt = startedAt, EndedAt = startedAt.AddMinutes(minutes), CreatedAt = now, UpdatedAt = now,
        };
        var sessions = new[]
        {
            Session(user.Id, now.AddMinutes(-90), 60),
            Session(user.Id, now.AddDays(-30), 120),
            Session(other.Id, now.AddMinutes(-50), 45), // Another user's play: never in this summary.
        };
        db.PlaySessions.AddRange(sessions);
        await db.SaveChangesAsync();
        var steamIntegration = new Integration
        {
            UserId = user.Id, Type = IntegrationType.Steam, PlatformId = steam.Id, AccountId = $"steam-{suffix}",
            ApiKey = $"steam-key-{suffix}", Active = true, CreatedAt = now, UpdatedAt = now,
        };

        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "key-that-does-not-exist");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/launcher/summary")).StatusCode);

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", integration.ApiKey);
            // The key reads only what is made for it: the account endpoints still need a signed-in user.
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/sessions")).StatusCode);

            var response = await client.GetAsync("/api/launcher/summary?tz=Europe/Prague");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(body);
            var data = json.RootElement.GetProperty("data");

            Assert.Equal("Europe/Prague", data.GetProperty("timeZone").GetString());
            Assert.Equal(180 * 60, data.GetProperty("allTime").GetProperty("totalSeconds").GetInt64());
            Assert.Equal(2, data.GetProperty("allTime").GetProperty("sessionCount").GetInt32());
            Assert.Equal(7, data.GetProperty("days").GetArrayLength());
            Assert.Equal(2, data.GetProperty("recentSessions").GetArrayLength());

            var played = data.GetProperty("games").EnumerateArray().Single();
            Assert.Equal(game.Title, played.GetProperty("title").GetString());
            Assert.Equal(180 * 60, played.GetProperty("totalSeconds").GetInt64());
            var steamIdentity = played.GetProperty("identities").EnumerateArray().Single();
            Assert.Equal("steam", steamIdentity.GetProperty("type").GetString());
            Assert.Equal(appId, steamIdentity.GetProperty("externalId").GetString());
            Assert.Equal("Steam", data.GetProperty("recentSessions")[0].GetProperty("platformName").GetString());
            foreach (var platform in data.GetProperty("periods").GetProperty("week").GetProperty("platforms").EnumerateArray())
            {
                Assert.Equal("Steam", platform.GetProperty("platformName").GetString());
            }

            // The yearly recap, as the web computes it, with the same key; only this user's play.
            var recap = await client.GetAsync($"/api/launcher/recap?year={now.UtcDateTime.Year}");
            Assert.Equal(HttpStatusCode.OK, recap.StatusCode);
            using var recapJson = JsonDocument.Parse(await recap.Content.ReadAsStringAsync());
            var recapData = recapJson.RootElement.GetProperty("data");
            Assert.Equal(sessions.Count(s => s.UserId == user.Id && s.StartedAt.UtcDateTime.Year == now.UtcDateTime.Year), recapData.GetProperty("totalSessions").GetInt32());
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/launcher/recap?year=1")).StatusCode);

            Assert.False(data.GetProperty("steamPolling").GetBoolean());

            // With Steam polling on, Playdex records Steam games itself and the launcher must not record them too.
            db.Integrations.Add(steamIntegration);
            await db.SaveChangesAsync();
            using var polled = JsonDocument.Parse(await client.GetStringAsync("/api/launcher/summary"));
            Assert.True(polled.RootElement.GetProperty("data").GetProperty("steamPolling").GetBoolean());
        }
        finally
        {
            if (steamIntegration.Id != 0)
            {
                db.Integrations.Remove(steamIntegration);
            }
            db.PlaySessions.RemoveRange(sessions);
            db.Integrations.Remove(integration);
            db.GameExternalIdentities.Remove(identity);
            db.Games.Remove(game);
            db.Users.RemoveRange(user, other);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RecapCountsTheYearInTheLaunchersTimeZone()
    {
        if (!postgres.IsConfigured) return;

        ConfigureHost();
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var steam = await db.Platforms.SingleAsync(platform => platform.Key == "steam");
        var user = new User { Email = $"launcher-recap-{suffix}@example.test", CreatedAt = now, UpdatedAt = now };
        var game = new Game { Title = $"Launcher Recap {suffix}", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id, Type = IntegrationType.Api, ApiKey = $"key-{suffix}", Active = true, CreatedAt = now, UpdatedAt = now,
        };
        db.Add(integration);
        await db.SaveChangesAsync();
        PlaySession Session(DateTimeOffset startedAt) => new()
        {
            GameId = game.Id, UserId = user.Id, PlatformId = steam.Id, CaptureMethod = CaptureMethod.Api,
            IntegrationId = integration.Id, StartedAt = startedAt, EndedAt = startedAt.AddMinutes(30),
            CreatedAt = now, UpdatedAt = now,
        };
        var sessions = new[]
        {
            // 20:00 UTC on Dec 31, 2025 is 05:00 on Jan 1, 2026 in Tokyo.
            Session(new DateTimeOffset(2025, 12, 31, 20, 0, 0, TimeSpan.Zero)),
            Session(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero)),
        };
        db.PlaySessions.AddRange(sessions);
        await db.SaveChangesAsync();

        try
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", integration.ApiKey);
            async Task<JsonElement> Recap(string query)
            {
                var response = await client.GetAsync($"/api/launcher/recap?{query}");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return json.RootElement.GetProperty("data").Clone();
            }

            Assert.Equal(1, (await Recap("year=2026&tz=Asia/Tokyo")).GetProperty("totalSessions").GetInt32());
            Assert.Equal(1, (await Recap("year=2025&tz=Asia%2FTokyo")).GetProperty("totalSessions").GetInt32());
            var tokyo2026 = await Recap("year=2026&tz=Asia/Tokyo");
            Assert.Equal(1, tokyo2026.GetProperty("sessionsByHour")[5].GetProperty("sessionCount").GetInt32());

            // Without a zone the year is UTC's, as before.
            Assert.Equal(0, (await Recap("year=2026")).GetProperty("totalSessions").GetInt32());
            Assert.Equal(2, (await Recap("year=2025")).GetProperty("totalSessions").GetInt32());
        }
        finally
        {
            db.PlaySessions.RemoveRange(sessions);
            db.Integrations.Remove(integration);
            db.Games.Remove(game);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    private static void ConfigureHost()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable("Auth__JwtSecret", "launcher-summary-integration-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(
                postgres.DataSource!,
                npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                })
            .Options);
}
