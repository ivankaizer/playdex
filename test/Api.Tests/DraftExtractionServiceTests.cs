using Api.Modules.Games;
using Api.Modules.Manual;
using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Api.Infrastructure.Http;
using Api.Modules.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class DraftExtractionServiceTests(PostgresFixture postgres)
{
    [Fact]
    public void ReferenceDateUsesUsersLocalDayRatherThanUtc()
    {
        var instant = new DateTimeOffset(2026,9,9,0,30,0,TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026,9,8), DraftExtractionService.ReferenceDate("America/Los_Angeles", instant));
        Assert.Equal(new DateOnly(2026,9,9), DraftExtractionService.ReferenceDate("Europe/Warsaw", instant));
    }
    [Fact]
    public void InvalidTimezoneIsRejected()
    {
        Assert.Throws<AppProblemException>(()=>DraftExtractionService.ReferenceDate("not-a-zone",DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task PreviouslyPlayedTitleVariantResolvesGameAndMostRecentPlatform()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Email = $"draft-match-{Guid.NewGuid():N}@test.local", CreatedAt = now, UpdatedAt = now };
        var otherUser = new User { Email = $"draft-match-other-{Guid.NewGuid():N}@test.local", CreatedAt = now, UpdatedAt = now };
        var game = new Game { Title = "Assassin's Creed Black Flag Resynced", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, otherUser, game);
        await db.SaveChangesAsync();
        var pc = await db.Platforms.SingleAsync(platform => platform.Key == "pc");
        var ps5 = await db.Platforms.SingleAsync(platform => platform.Key == "ps5");
        db.PlaySessions.AddRange(
            Session(user.Id, game.Id, pc.Id, now.AddDays(-2)),
            Session(user.Id, game.Id, ps5.Id, now.AddDays(-1)),
            Session(otherUser.Id, game.Id, pc.Id, now));
        await db.SaveChangesAsync();

        var igdbOptions = Options.Create(new IgdbOptions());
        var manualRepository = new ManualRepository(db);
        var draftRepository = new DraftRepository(db);
        var manual = new ManualService(
            manualRepository,
            new IgdbClient(new HttpClient(), igdbOptions),
            igdbOptions,
            new GameResolver(db));
        var drafts = new DraftService(draftRepository, db, manual, manualRepository);
        var service = new DraftExtractionService(
            draftRepository,
            drafts,
            manualRepository,
            new StubExtractor([new("Assassin's Creed IV: Black Flag", "2026-09-09", 60, "unknown", ["Platform: not indicated."])]),
            Options.Create(new AiDraftOptions { Enabled = true, ApiKey = "test", Model = "test" }),
            postgres.DataSource!,
            TimeProvider.System);

        var batch = await service.Create(
            user.Id,
            Guid.NewGuid(),
            "UTC",
            "Black Flag for one hour",
            null,
            null,
            CancellationToken.None);

        var row = Assert.Single(batch.Rows);
        Assert.Equal("Assassin's Creed Black Flag Resynced", row.Selection?.Title);
        Assert.Equal("local", row.Selection?.Source);
        Assert.Equal("ps5", row.PlatformKey);
        Assert.DoesNotContain(row.Warnings, warning => warning.StartsWith("Platform:", StringComparison.Ordinal));
        Assert.True(row.Ready);
    }

    private static PlaySession Session(long userId, long gameId, long platformId, DateTimeOffset startedAt) => new()
    {
        UserId = userId,
        GameId = gameId,
        PlatformId = platformId,
        CaptureMethod = CaptureMethod.Manual,
        StartedAt = startedAt,
        EndedAt = startedAt.AddHours(1),
        CreatedAt = startedAt,
        UpdatedAt = startedAt,
    };

    private sealed class StubExtractor(IReadOnlyList<ExtractedSession> rows) : IPlaySessionExtractor
    {
        public Task<IReadOnlyList<ExtractedSession>> Extract(
            ExtractionInput input,
            CancellationToken cancellationToken) => Task.FromResult(rows);
    }

    private PlaydexDbContext CreateDb() => new(new DbContextOptionsBuilder<PlaydexDbContext>()
        .UseNpgsql(postgres.DataSource!, options =>
        {
            options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            options.MapEnum<IntegrationType>("integration_type");
            options.MapEnum<CaptureMethod>("capture_method");
        }).Options);
}
