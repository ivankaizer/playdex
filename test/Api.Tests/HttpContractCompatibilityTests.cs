using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class HttpContractCompatibilityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task KnownDefaultsValidationMalformedJsonAndRecapRangeMatchArchive()
    {
        if (!postgres.IsConfigured) return;

        await using var db = new PlaydexDbContext(
            new DbContextOptionsBuilder<PlaydexDbContext>()
                .UseNpgsql(postgres.DataSource!)
                .Options);
        var suffix = Guid.NewGuid().ToString("n");
        var password = "contract-password";
        var user = new User
        {
            Email = $"http-{suffix}@example.test",
            Password = BCrypt.Net.BCrypt.HashPassword(password),
        };
        db.Users.Add(user);
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var games = Enumerable.Range(1, 12).Select(index => new Game
        {
            PlatformId = steam.Id,
            Title = $"contract-{suffix}-{index:D2}",
            CanonicalKey = $"steam:contract-{suffix}-{index:D2}",
        }).ToList();
        db.Games.AddRange(games);
        await db.SaveChangesAsync();

        try
        {
            ConfigureHost();
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/login", new
            {
                email = user.Email,
                password,
            });
            var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                loginJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString());

            var search = await client.GetAsync($"/api/games/search?query=contract-{suffix}");
            var searchJson = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
            var invalidLimit = await client.GetAsync("/api/sessions?limit=0");
            var invalidOffset = await client.GetAsync("/api/sessions?offset=-1");
            var invalidAccount = await client.GetAsync("/api/accounts/not-a-number/sessions");
            using var malformedRequest = new HttpRequestMessage(HttpMethod.Post, "/api/games/ignored")
            {
                Content = new StringContent("{", Encoding.UTF8, "application/json"),
            };
            var malformed = await client.SendAsync(malformedRequest);
            var partialRecap = await client.GetAsync("/api/recap?from=2026-01-01");
            var forbiddenAdmin = await client.GetAsync("/api/admin/sessions");
            var recap = await client.GetAsync(
                "/api/recap?from=2026-01-01T12:30:00Z&to=2026-01-02");
            var recapJson = JsonDocument.Parse(await recap.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, search.StatusCode);
            Assert.Equal(10,
                searchJson.RootElement.GetProperty("data").GetProperty("results").GetArrayLength());
            Assert.Equal(HttpStatusCode.BadRequest, invalidLimit.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, invalidOffset.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, invalidAccount.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            Assert.Contains("Invalid JSON body", await malformed.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest, partialRecap.StatusCode);
            Assert.Contains("from and to are required", await partialRecap.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenAdmin.StatusCode);
            Assert.Contains("Admin access required", await forbiddenAdmin.Content.ReadAsStringAsync());
            Assert.Equal("2026-01-01T12:30:00Z",
                recapJson.RootElement.GetProperty("data").GetProperty("range").GetProperty("from").GetString());
            Assert.Equal("2026-01-02",
                recapJson.RootElement.GetProperty("data").GetProperty("range").GetProperty("to").GetString());
        }
        finally
        {
            db.Users.Remove(user);
            db.Games.RemoveRange(games);
            await db.SaveChangesAsync();
        }
    }

    private static void ConfigureHost()
    {
        Environment.SetEnvironmentVariable(
            "DATABASE_URL",
            Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL"));
        Environment.SetEnvironmentVariable(
            "Auth__JwtSecret", "http-contract-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }
}
