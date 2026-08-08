using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Api.Shared.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class GameStatsCompatibilityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GameStats_ReturnsYearlyAndMonthlyBucketsScopedToTheGame()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User { Email = $"stats-{suffix}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = steam.Id,
            ApiKey = $"key-{suffix}",
            Active = true,
        };
        var game = new Game
        {
            PlatformId = steam.Id,
            Title = "Stats Game",
            ExternalId = $"42{suffix}",
            CanonicalKey = $"steam:42{suffix}",
        };
        var otherGame = new Game
        {
            PlatformId = steam.Id,
            Title = "Other Game",
            ExternalId = $"99{suffix}",
            CanonicalKey = $"steam:99{suffix}",
        };
        db.AddRange(credential, game, otherGame);
        await db.SaveChangesAsync();
        db.PlaySessions.AddRange(
            EndedSession(credential.Id, game.Id, new DateTimeOffset(2025, 3, 10, 10, 0, 0, TimeSpan.Zero), 3600),
            EndedSession(credential.Id, game.Id, new DateTimeOffset(2026, 6, 5, 9, 0, 0, TimeSpan.Zero), 5400),
            EndedSession(credential.Id, otherGame.Id, new DateTimeOffset(2026, 6, 5, 9, 0, 0, TimeSpan.Zero), 999999));
        await db.SaveChangesAsync();

        try
        {
            ConfigureHost();
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credential.ApiKey);

            var gameId = await db.Games
                .AsNoTracking()
                .Where(row => row.CanonicalKey == $"steam:42{suffix}")
                .Select(row => row.Id)
                .SingleAsync();

            var response = await client.GetAsync($"/api/games/{gameId}/stats");
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(2, data.GetProperty("sessionCount").GetInt32());
            Assert.Equal(9000, data.GetProperty("totalSeconds").GetInt32());
            Assert.Equal("2026-06-05T09:00:00+00:00",
                data.GetProperty("lastPlayedAt").GetString());

            var years = data.GetProperty("years");
            var year2025 = Enumerable.Range(0, years.GetArrayLength())
                .Select(i => years[i])
                .Single(item => item.GetProperty("year").GetInt32() == 2025);
            Assert.Equal(3600, year2025.GetProperty("seconds").GetInt32());

            var months = data.GetProperty("months");
            var june2026 = Enumerable.Range(0, months.GetArrayLength())
                .Select(i => months[i])
                .Single(item =>
                    item.GetProperty("year").GetInt32() == 2026 &&
                    item.GetProperty("month").GetInt32() == 6);
            Assert.Equal(5400, june2026.GetProperty("seconds").GetInt32());
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            await db.Games
                .Where(row => row.CanonicalKey.EndsWith(suffix))
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GameStats_ReturnsEmptyBucketsForAGameWithNoSessions()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User { Email = $"stats-empty-{suffix}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = steam.Id,
            ApiKey = $"key-{suffix}",
            Active = true,
        };
        var game = new Game
        {
            PlatformId = steam.Id,
            Title = "Empty Game",
            ExternalId = $"7{suffix}",
            CanonicalKey = $"steam:7{suffix}",
        };
        db.AddRange(credential, game);
        await db.SaveChangesAsync();

        try
        {
            ConfigureHost();
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credential.ApiKey);

            var response = await client.GetAsync($"/api/games/{game.Id}/stats");
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var data = json.RootElement.GetProperty("data");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(0, data.GetProperty("sessionCount").GetInt32());
            Assert.Equal(0, data.GetProperty("totalSeconds").GetInt32());
            Assert.Equal(JsonValueKind.Null, data.GetProperty("lastPlayedAt").ValueKind);
            Assert.Empty(data.GetProperty("years").EnumerateArray());
            Assert.Empty(data.GetProperty("months").EnumerateArray());
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            await db.Games
                .Where(row => row.CanonicalKey.EndsWith(suffix))
                .ExecuteDeleteAsync();
        }
    }

    private static PlaySession EndedSession(
        long credentialId,
        long gameId,
        DateTimeOffset startedAt,
        double seconds)
    {
        var endedAt = startedAt.AddSeconds(seconds);
        return new PlaySession
        {
            PlatformCredentialId = credentialId,
            GameId = gameId,
            StartedAt = startedAt,
            EndedAt = endedAt,
            CreatedAt = startedAt,
            UpdatedAt = endedAt,
        };
    }

    private static void ConfigureHost()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable(
            "JWT_SECRET", "game-stats-integration-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);
}
