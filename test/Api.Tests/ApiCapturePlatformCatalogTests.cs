using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ApiCapturePlatformCatalogTests(PostgresFixture postgres)
{
    [Fact]
    public async Task CatalogRequiresApiKeyAndReturnsRecursivePcHierarchy()
    {
        if (!postgres.IsConfigured) return;

        ConfigureHost();
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/capture/platforms")).StatusCode);

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var pc = await db.Platforms.SingleAsync(platform => platform.Key == "pc");
        var steam = await db.Platforms.SingleAsync(platform => platform.Key == "steam");
        var child = new Platform
        {
            Key = $"test-child-{suffix}",
            Name = "A nested PC child",
            ParentId = steam.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var user = new User
        {
            Email = $"capture-platforms-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AddRange(child, user);
        await db.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Api,
            ApiKey = $"key-{suffix}",
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        try
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", integration.ApiKey);
            var response = await client.GetAsync("/api/capture/platforms");
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(body);
            var platforms = json.RootElement.GetProperty("data").EnumerateArray()
                .Select(item => (
                    Key: item.GetProperty("key").GetString()!,
                    Name: item.GetProperty("name").GetString()!))
                .ToList();

            Assert.Equal("pc", platforms[0].Key);
            Assert.Contains(platforms, platform => platform.Key == "steam");
            Assert.Contains(platforms, platform => platform.Key == "gog");
            Assert.Contains(platforms, platform => platform.Key == child.Key);
            Assert.DoesNotContain(platforms, platform => platform.Key == "playnite");
            Assert.Equal(
                platforms.Skip(1).OrderBy(platform => platform.Name).ThenBy(platform => platform.Key),
                platforms.Skip(1));
            Assert.Equal(pc.Id, (await db.Platforms.SingleAsync(platform => platform.Key == "gog")).ParentId);
        }
        finally
        {
            db.Integrations.Remove(integration);
            db.Platforms.Remove(child);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    private static void ConfigureHost()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable(
            "Auth__JwtSecret",
            "platform-catalog-integration-secret-at-least-thirty-two-bytes");
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
