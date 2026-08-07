using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Playdex.Api.Shared.Persistence;
using Xunit;

namespace Playdex.Api.Tests;

[Collection(PostgresCollection.Name)]
public sealed class PlayniteCompatibilityIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EndAndPing_PreserveArchivedMatchingGameSemanticsAndEnvelope()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var playnite = await db.Platforms.SingleAsync(row => row.Key == "playnite");
        var steam = await db.Platforms.SingleAsync(row => row.Key == "steam");
        var user = new User { Email = $"playnite-{suffix}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var credential = new PlatformCredential
        {
            UserId = user.Id,
            PlatformId = playnite.Id,
            ApiKey = $"key-{suffix}",
            Active = true,
        };
        var firstGame = new Game
        {
            PlatformId = steam.Id,
            Title = "First",
            ExternalId = $"101{suffix}",
            CanonicalKey = $"steam:101{suffix}",
        };
        var secondGame = new Game
        {
            PlatformId = steam.Id,
            Title = "Second",
            ExternalId = $"202{suffix}",
            CanonicalKey = $"steam:202{suffix}",
        };
        db.AddRange(credential, firstGame, secondGame);
        await db.SaveChangesAsync();
        db.PlaySessions.AddRange(
            ActiveSession(credential.Id, firstGame.Id),
            ActiveSession(credential.Id, secondGame.Id));
        await db.SaveChangesAsync();

        try
        {
            ConfigureHost();
            await using var factory = new WebApplicationFactory<Program>();
            using var client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", credential.ApiKey);

            var end = await client.PostAsJsonAsync("/api/playnite/sessions/end", new
            {
                gameTitle = "First",
                gameId = $"101{suffix}",
                gameProvider = "steam",
            });
            var endBody = await end.Content.ReadAsStringAsync();
            var endJson = JsonDocument.Parse(endBody);

            Assert.True(end.StatusCode == HttpStatusCode.OK, endBody);
            Assert.Null(endJson.RootElement.GetProperty("error").GetString());
            Assert.Equal(1,
                endJson.RootElement.GetProperty("data").GetProperty("endedSessionCount").GetInt32());
            await db.Entry(firstGame).ReloadAsync();
            var sessions = await db.PlaySessions
                .AsNoTracking()
                .Where(row => row.PlatformCredentialId == credential.Id)
                .OrderBy(row => row.Id)
                .ToListAsync();
            Assert.NotNull(sessions.Single(row => row.GameId == firstGame.Id).EndedAt);
            Assert.Null(sessions.Single(row => row.GameId == secondGame.Id).EndedAt);

            var ping = await client.PostAsJsonAsync("/api/playnite/sessions/ping", new
            {
                gameTitle = "Third",
                gameId = 303,
                gameProvider = "steam",
            });
            var pingJson = JsonDocument.Parse(await ping.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.OK, ping.StatusCode);
            Assert.True(pingJson.RootElement.GetProperty("data").GetProperty("started").GetBoolean());
            Assert.Equal(0,
                pingJson.RootElement.GetProperty("data").GetProperty("endedSessionCount").GetInt32());
            Assert.Equal(2, await db.PlaySessions.AsNoTracking().CountAsync(row =>
                row.PlatformCredentialId == credential.Id && row.EndedAt == null));

            var retainedStart = await client.PostAsJsonAsync("/api/playnite/sessions/start", new
            {
                gameTitle = "Retained Start",
                gameId = $"start-{suffix}",
                gameProvider = "unknown",
            });
            var retainedRecord = await client.PostAsJsonAsync("/api/playnite/sessions", new
            {
                durationInSeconds = 60,
                gameTitle = "Retained Record",
                gameId = $"record-{suffix}",
                gameProvider = "unknown",
            });
            Assert.Equal(HttpStatusCode.OK, retainedStart.StatusCode);
            Assert.Equal(HttpStatusCode.OK, retainedRecord.StatusCode);
        }
        finally
        {
            db.Users.Remove(user);
            await db.SaveChangesAsync();
            await db.Games
                .Where(row => row.CanonicalKey.EndsWith(suffix) || row.CanonicalKey == "steam:303")
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task End_ValidatesAuthenticationPayloadAndProviderCompatibly()
    {
        if (!postgres.IsConfigured) return;

        ConfigureHost();
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var validBody = new { gameTitle = "Hades", gameId = 123, gameProvider = "steam" };

        var missingKey = await client.PostAsJsonAsync("/api/playnite/sessions/end", validBody);
        using var empty = new HttpRequestMessage(HttpMethod.Post, "/api/playnite/sessions/end");
        empty.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-key");
        var missingPayload = await client.SendAsync(empty);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "invalid-key");
        var invalidProvider = await client.PostAsJsonAsync("/api/playnite/sessions/end", new
        {
            gameTitle = "Hades",
            gameId = 123,
            gameProvider = "psn",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, missingKey.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingPayload.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidProvider.StatusCode);
        Assert.Contains("gameTitle is required", await missingPayload.Content.ReadAsStringAsync());
        Assert.Contains("gameProvider must be steam or unknown", await invalidProvider.Content.ReadAsStringAsync());
    }

    private static PlaySession ActiveSession(long credentialId, long gameId)
    {
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new PlaySession
        {
            PlatformCredentialId = credentialId,
            GameId = gameId,
            StartedAt = startedAt,
            CreatedAt = startedAt,
            UpdatedAt = startedAt,
        };
    }

    private static void ConfigureHost()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")!;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable(
            "JWT_SECRET", "playnite-integration-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!)
            .Options);
}
