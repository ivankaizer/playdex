using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Api.Infrastructure.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ProviderCredentialCompatibilityTests(PostgresFixture postgres)
{
    [Fact]
    public async Task UserCanUpdateAccountIdAndApiKeyUnlessTheKeyIsLocked()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var password = "provider-password";
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User
        {
            Email = $"provider-{suffix}@example.test",
            Password = BCrypt.Net.BCrypt.HashPassword(password),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var editable = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = steam.Id,
            ApiKey = "editable-key",
            AccountId = "account-1",
            ApiKeyLocked = false,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var locked = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = steam.Id,
            ApiKey = "locked-key",
            AccountId = "account-2",
            ApiKeyLocked = true,
            Active = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.AddRange(editable, locked);
        await db.SaveChangesAsync();

        try
        {
            ConfigureHost();
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            var login = await client.PostAsJsonAsync("/api/login", new
            {
                email = user.Email,
                password = password,
            });
            var loginJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                loginJson.RootElement.GetProperty("data").GetProperty("accessToken").GetString());

            var list = await client.GetAsync("/api/providers");
            var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var providers = listJson.RootElement.GetProperty("data").GetProperty("providers");
            var editableDto = providers.EnumerateArray().Single(item =>
                item.GetProperty("id").GetInt64() == editable.Id);
            var lockedDto = providers.EnumerateArray().Single(item =>
                item.GetProperty("id").GetInt64() == locked.Id);

            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal("editable-key", editableDto.GetProperty("apiKey").GetString());
            Assert.False(editableDto.GetProperty("apiKeyLocked").GetBoolean());
            Assert.Equal(JsonValueKind.Null, lockedDto.GetProperty("apiKey").ValueKind);
            Assert.True(lockedDto.GetProperty("apiKeyLocked").GetBoolean());
            Assert.Equal("account-2", lockedDto.GetProperty("accountId").GetString());

            var update = await client.PutAsJsonAsync(
                $"/api/providers/{editable.Id}",
                new { apiKey = "new-key", accountId = "new-account" });
            Assert.Equal(HttpStatusCode.OK, update.StatusCode);
            var updatedJson = JsonDocument.Parse(await update.Content.ReadAsStringAsync());
            var updated = updatedJson.RootElement.GetProperty("data").GetProperty("provider");
            Assert.Equal("new-key", updated.GetProperty("apiKey").GetString());
            Assert.Equal("new-account", updated.GetProperty("accountId").GetString());

            var lockedUpdate = await client.PutAsJsonAsync(
                $"/api/providers/{locked.Id}",
                new { accountId = "account-changed" });
            Assert.Equal(HttpStatusCode.OK, lockedUpdate.StatusCode);

            var forbiddenKeyUpdate = await client.PutAsJsonAsync(
                $"/api/providers/{locked.Id}",
                new { apiKey = "hacked" });
            Assert.Equal(HttpStatusCode.BadRequest, forbiddenKeyUpdate.StatusCode);
            Assert.Contains("managed by an administrator",
                await forbiddenKeyUpdate.Content.ReadAsStringAsync());
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    private static void ConfigureHost()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable(
            "JWT_SECRET", "provider-credential-integration-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);
}
