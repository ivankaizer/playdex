using Api.Infrastructure.Http;
using Api.Modules.Polling;
using Api.Modules.Privacy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PrivacyIntegrationTests(PostgresFixture postgres)
{
    private const string Password = "correct horse";

    [Fact]
    public async Task ExportIncludesUserDataButNeverCredentials()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await SeedAsync(db);

        var export = await CreateService(db).Export(seed.User.Id, CancellationToken.None);

        Assert.Equal(seed.User.Email, export.Profile.Email);
        Assert.Equal(15, export.Settings.IgnoreShortSessionsMinutes);
        Assert.Equal("12345", Assert.Single(export.NotificationChannels).Destination);
        var integration = Assert.Single(export.Integrations);
        Assert.True(integration.HasApiKey);
        Assert.Equal("steam-id", integration.AccountId);
        var session = Assert.Single(export.Sessions);
        Assert.Equal(seed.Game.Title, session.Game);
        Assert.Equal("polling", session.CaptureMethod);
        Assert.Equal(4, Assert.Single(export.Ratings).Rating);
        Assert.Equal("pasted text", Assert.Single(export.AiGenerations).InputText);
        Assert.Equal(Convert.ToBase64String([1, 2, 3]), export.AiGenerations[0].ImageBase64);
        Assert.Equal("Draft title", Assert.Single(Assert.Single(export.DraftBatches).Rows).Title);

        var json = System.Text.Json.JsonSerializer.Serialize(export);
        Assert.DoesNotContain("secret-api-key", json);
    }

    [Fact]
    public async Task DeleteRejectsWrongPasswordAndKeepsData()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await SeedAsync(db);
        var service = CreateService(db);

        var wrong = await Assert.ThrowsAsync<AppProblemException>(
            () => service.DeleteAccount(seed.User.Id, "nope", CancellationToken.None));
        Assert.Equal(403, wrong.StatusCode);
        var missing = await Assert.ThrowsAsync<AppProblemException>(
            () => service.DeleteAccount(seed.User.Id, "", CancellationToken.None));
        Assert.Equal(400, missing.StatusCode);

        Assert.True(await db.Users.AnyAsync(user => user.Id == seed.User.Id));
        Assert.True(await db.PlaySessions.AnyAsync(row => row.UserId == seed.User.Id));
    }

    [Fact]
    public async Task DeleteRemovesEverythingTheUserOwnsButKeepsSharedGames()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await SeedAsync(db);
        var other = await SeedAsync(db);

        await CreateService(db).DeleteAccount(seed.User.Id, Password, CancellationToken.None);

        var id = seed.User.Id;
        Assert.False(await db.Users.AnyAsync(user => user.Id == id));
        Assert.False(await db.PlaySessions.AnyAsync(row => row.UserId == id));
        Assert.False(await db.Integrations.AnyAsync(row => row.UserId == id));
        Assert.False(await db.PollResults.AnyAsync(row => row.IntegrationId == seed.IntegrationId));
        Assert.False(await db.NotificationChannels.AnyAsync(row => row.UserId == id));
        Assert.False(await db.UserSettings.AnyAsync(row => row.UserId == id));
        Assert.False(await db.GameRatings.AnyAsync(row => row.UserId == id));
        Assert.False(await db.SessionDraftBatches.AnyAsync(row => row.UserId == id));
        Assert.False(await db.AiGenerations.AnyAsync(row => row.UserId == id));

        Assert.True(await db.Games.AnyAsync(game => game.Id == seed.Game.Id));
        Assert.True(await db.Users.AnyAsync(user => user.Id == other.User.Id));
        Assert.True(await db.PlaySessions.AnyAsync(row => row.UserId == other.User.Id));
        Assert.True(await db.PollResults.AnyAsync(row => row.IntegrationId == other.IntegrationId));
    }

    [Fact]
    public async Task PollResultRetentionDeletesOnlyExpiredRows()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var seed = await SeedAsync(db);
        var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        db.PollResults.AddRange(
            new PollResult { IntegrationId = seed.IntegrationId, PolledAt = now.AddDays(-31), RawPayload = "{}", CreatedAt = now },
            new PollResult { IntegrationId = seed.IntegrationId, PolledAt = now.AddDays(-29), RawPayload = "{}", CreatedAt = now });
        await db.SaveChangesAsync();

        // The retention command runs on its own connection, so it can only see committed rows.
        await transaction.CommitAsync();
        try
        {
            var retention = new PollResultRetention(
                postgres.DataSource!, Options.Create(new PollingOptions { PollResultRetentionDays = 30 }), new FixedClock(now));
            await retention.DeleteExpired(CancellationToken.None);

            var remaining = await db.PollResults.AsNoTracking()
                .Where(row => row.IntegrationId == seed.IntegrationId)
                .Select(row => row.PolledAt)
                .ToListAsync();
            Assert.DoesNotContain(now.AddDays(-31), remaining);
            Assert.Contains(now.AddDays(-29), remaining);

            var keepForever = new PollResultRetention(
                postgres.DataSource!, Options.Create(new PollingOptions { PollResultRetentionDays = 0 }), new FixedClock(now));
            Assert.Equal(0, await keepForever.DeleteExpired(CancellationToken.None));
        }
        finally
        {
            await CreateService(db).DeleteAccount(seed.User.Id, Password, CancellationToken.None);
        }
    }

    private PlaydexDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            }).Options);

    private static PrivacyService CreateService(PlaydexDbContext db) =>
        new(new PrivacyRepository(db), TimeProvider.System);

    private sealed record Seed(User User, Game Game, long IntegrationId);

    private static async Task<Seed> SeedAsync(PlaydexDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"privacy-{suffix}@example.test", Password = BCrypt.Net.BCrypt.HashPassword(Password) };
        var game = new Game { Title = $"Privacy game {suffix}" };
        var platform = new Platform { Key = $"privacy-{suffix}", Name = "Privacy platform" };
        db.AddRange(user, game, platform);
        await db.SaveChangesAsync();

        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Steam,
            PlatformId = platform.Id,
            ApiKey = "secret-api-key",
            AccountId = "steam-id",
            Active = true,
        };
        db.Integrations.Add(integration);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        db.PlaySessions.Add(new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            IntegrationId = integration.Id,
            CaptureMethod = CaptureMethod.Polling,
            StartedAt = now.AddHours(-2),
            EndedAt = now.AddHours(-1),
        });
        db.PollResults.Add(new PollResult { IntegrationId = integration.Id, PolledAt = now, RawPayload = "{}", CreatedAt = now });
        db.NotificationChannels.Add(new NotificationChannel { UserId = user.Id, Transport = "telegram", Destination = "12345" });
        db.UserSettings.Add(new UserSettings { UserId = user.Id, IgnoreShortSessionsMinutes = 15 });
        db.GameRatings.Add(new GameRating { UserId = user.Id, GameId = game.Id, Rating = 4 });
        db.SessionDraftBatches.Add(new SessionDraftBatch
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RequestId = Guid.NewGuid(),
            CreatedAt = now,
            Timezone = "UTC",
            ReferenceDate = DateOnly.FromDateTime(now.UtcDateTime),
            Rows = [new SessionDraftRow { Id = Guid.NewGuid(), Position = 0, Title = "Draft title" }],
        });
        db.AiGenerations.Add(new AiGeneration
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RequestId = Guid.NewGuid(),
            CreatedAt = now,
            Provider = "openai",
            Model = "test-model",
            Timezone = "UTC",
            ReferenceDate = DateOnly.FromDateTime(now.UtcDateTime),
            InputText = "pasted text",
            Image = [1, 2, 3],
            ImageContentType = "image/png",
            Status = "success",
        });
        await db.SaveChangesAsync();
        return new Seed(user, game, integration.Id);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
