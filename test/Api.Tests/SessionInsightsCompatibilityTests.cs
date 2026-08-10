using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.Infrastructure.Persistence;
using Api.Modules.Accounts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class SessionInsightsCompatibilityTests(PostgresFixture postgres)
{
    private static readonly string?[] ExpectedMappedPlatforms = ["psn", "ps5"];
    private static readonly string?[] ExpectedFacetPlatforms = ["ps5", "steam"];
    private static readonly int[] ExpectedMonths = [1, 2, 3];
    private static readonly double[] ExpectedMonthTotals = [7200d, 0d, 0d];

    [Theory]
    [InlineData("psn", "ps5")]
    [InlineData("PS5", "ps5")]
    [InlineData(" steam ", "steam")]
    public void PlatformKeysAreNormalized(string input, string expected) =>
        Assert.Equal(expected, AccountsRepository.NormalizePlatformKey(input));

    [Fact]
    public async Task PlatformFilterUsesDisplayedPlatformAndStatsKeepTheFullFacet()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var password = "session-insights-password";
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var psn = await db.Platforms.SingleAsync(row => row.Key == "psn");
        var manual = await db.Platforms.SingleOrDefaultAsync(row => row.Key == "manual");
        var createdManualPlatform = manual is null;
        manual ??= new Platform { Key = "manual", Name = "Manual" };
        if (createdManualPlatform)
        {
            db.Platforms.Add(manual);
            await db.SaveChangesAsync();
        }
        var user = new User
        {
            Email = $"session-insights-{suffix}@example.test",
            Password = BCrypt.Net.BCrypt.HashPassword(password),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var psnCredential = Credential(user.Id, psn.Id, $"psn-{suffix}");
        var manualCredential = Credential(user.Id, manual.Id, $"manual-{suffix}");
        var steamCredential = Credential(user.Id, steam.Id, $"steam-{suffix}");
        var psnGame = Game(psn.Id, "PSN Game", $"psn:{suffix}");
        var mappedGame = Game(psn.Id, "Mapped Game", $"ps5:{suffix}");
        var manualGame = Game(manual.Id, "Mapped Game", $"manual:{suffix}");
        var steamGame = Game(steam.Id, "Steam Game", $"steam:{suffix}");
        db.AddRange(psnCredential, manualCredential, steamCredential, psnGame, mappedGame, manualGame, steamGame);
        await db.SaveChangesAsync();
        db.ManualGameInputs.Add(new ManualGameInput
        {
            UserId = user.Id,
            RawTitle = "Mapped Game",
            CanonicalTitle = "mapped game",
            MappedGameId = mappedGame.Id,
        });
        db.PlaySessions.AddRange(
            EndedSession(psnCredential.Id, psnGame.Id, new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero), 3600),
            EndedSession(manualCredential.Id, manualGame.Id, new DateTimeOffset(2026, 1, 31, 23, 30, 0, TimeSpan.Zero), 3600),
            EndedSession(steamCredential.Id, steamGame.Id, new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.Zero), 7200));
        await db.SaveChangesAsync();

        try
        {
            ConfigureHost();
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/login", new { email = user.Email, password });
            var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                loginJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString());

            var sessionsResponse = await client.GetAsync(
                "/api/sessions?platform=ps5&from=2026-01-01&to=2026-03-31&limit=10&offset=0");
            var sessionsData = JsonDocument.Parse(await sessionsResponse.Content.ReadAsStringAsync())
                .RootElement.GetProperty("data");
            Assert.Equal(HttpStatusCode.OK, sessionsResponse.StatusCode);
            Assert.Equal(2, sessionsData.GetProperty("total").GetInt32());
            Assert.All(sessionsData.GetProperty("sessions").EnumerateArray(), session =>
            {
                var platform = session.GetProperty("mappedPlatform").ValueKind == JsonValueKind.Null
                    ? session.GetProperty("platform").GetString()
                    : session.GetProperty("mappedPlatform").GetString();
                Assert.Contains(platform, ExpectedMappedPlatforms);
            });

            var statsResponse = await client.GetAsync(
                "/api/stats?from=2026-01-01&to=2026-03-31&platform=ps5");
            var stats = JsonDocument.Parse(await statsResponse.Content.ReadAsStringAsync())
                .RootElement.GetProperty("data").GetProperty("stats");
            Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
            Assert.Equal(2, stats.GetProperty("sessionCount").GetInt32());
            Assert.Equal(7200d, stats.GetProperty("totalSeconds").GetDouble());
            Assert.Equal(ExpectedFacetPlatforms, stats.GetProperty("platforms")
                .EnumerateArray().Select(item => item.GetProperty("platform").GetString()).ToArray());

            var months = stats.GetProperty("months").EnumerateArray().ToArray();
            Assert.Equal(3, months.Length);
            Assert.Equal(ExpectedMonths, months.Select(item => item.GetProperty("month").GetInt32()).ToArray());
            Assert.Equal(ExpectedMonthTotals, months.Select(item => item.GetProperty("totalSeconds").GetDouble()).ToArray());
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            await db.Games.Where(row => row.CanonicalKey.EndsWith(suffix)).ExecuteDeleteAsync();
            if (createdManualPlatform)
            {
                await db.Platforms.Where(row => row.Id == manual.Id).ExecuteDeleteAsync();
            }
        }
    }

    private static PlatformCredential Credential(long userId, long platformId, string apiKey) => new()
    {
        UserId = userId,
        PlatformId = platformId,
        ApiKey = apiKey,
        Active = true,
    };

    private static Game Game(long platformId, string title, string canonicalKey) => new()
    {
        PlatformId = platformId,
        Title = title,
        CanonicalKey = canonicalKey,
    };

    private static PlaySession EndedSession(
        long credentialId,
        long gameId,
        DateTimeOffset startedAt,
        double seconds) => new()
    {
        PlatformCredentialId = credentialId,
        GameId = gameId,
        StartedAt = startedAt,
        EndedAt = startedAt.AddSeconds(seconds),
        CreatedAt = startedAt,
        UpdatedAt = startedAt.AddSeconds(seconds),
    };

    private static void ConfigureHost()
    {
        Environment.SetEnvironmentVariable(
            "DATABASE_URL",
            Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL"));
        Environment.SetEnvironmentVariable(
            "Auth__JwtSecret", "session-insights-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);
}
