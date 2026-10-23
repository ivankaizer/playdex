using Api.Modules.Games;
using Api.Modules.Manual;
using Api.Modules.Manual.Drafts;
using Api.Modules.Manual.Drafts.Extraction;
using Api.Infrastructure.Http;
using Api.Modules.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
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
            new GameResolver(db),
            TestSessions.Lifecycle(db));
        var drafts = new DraftService(draftRepository, db, manual, manualRepository);
        var stub = new StubExtractor([new("Assassin's Creed IV: Black Flag", "2026-09-09", 60, "unknown", ["Platform: not indicated."])]);
        var service = new DraftExtractionService(
            draftRepository,
            drafts,
            manualRepository,
            stub,
            Options.Create(new AiDraftOptions { Enabled = true, ApiKey = "test", Model = "test" }),
            postgres.DataSource!,
            TimeProvider.System,
            new GenerationHistoryRecorder(postgres.DataSource!, Options.Create(new AiDraftOptions { Provider="test", Model="test" }),
                TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<GenerationHistoryRecorder>.Instance));

        var requestId = Guid.NewGuid();
        var batch = await service.Create(
            user.Id,
            requestId,
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
        var history = await db.AiGenerations.AsNoTracking().SingleAsync(generation => generation.UserId == user.Id);
        Assert.Equal("success", history.Status);
        Assert.Equal("Black Flag for one hour", history.InputText);
        Assert.Contains("Assassin's Creed Black Flag Resynced", history.ResultJson);
        Assert.Contains("ps5", history.ResultJson);
        await service.Create(user.Id, requestId, "UTC", "Different replay input", null, null, default);
        Assert.Equal(1, await db.AiGenerations.CountAsync(generation => generation.UserId == user.Id));
        await db.SessionDraftRows.Where(draft => draft.Id == row.Id).ExecuteUpdateAsync(setters => setters.SetProperty(draft => draft.Title, "Edited by user"));
        Assert.Equal(history.ResultJson, (await db.AiGenerations.AsNoTracking().SingleAsync(generation => generation.Id == history.Id)).ResultJson);
        stub.Error = new AppProblemException(502, "AI returned invalid extraction.");
        await Assert.ThrowsAsync<AppProblemException>(() => service.Create(user.Id,Guid.NewGuid(),"UTC","Failed evidence",null,null,default));
        var failed = await db.AiGenerations.AsNoTracking().SingleAsync(generation => generation.UserId == user.Id && generation.Status == "failure");
        Assert.Equal("Failed evidence",failed.InputText);
        Assert.Null(failed.ResultJson);
        stub.Error = new AppProblemException(504, "AI extraction timed out.");
        await Assert.ThrowsAsync<AppProblemException>(() => service.Create(user.Id,Guid.NewGuid(),"UTC","Timeout evidence",null,null,default));
        Assert.True(await db.AiGenerations.AnyAsync(generation => generation.UserId == user.Id && generation.Status == "timeout"));
    }

    [Fact]
    public async Task ScreenshotIsNotDecodedWhileAnotherExtractionHoldsTheUsersLock()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var user = await CreateUser(db, "draft-lock");
        var stub = new StubExtractor([]);
        var service = CreateService(db, stub);

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using (var acquire = new NpgsqlCommand("select pg_advisory_lock(hashtextextended(@key,0))", connection))
        {
            acquire.Parameters.AddWithValue("key", $"draft-extraction-user:{user.Id}");
            await acquire.ExecuteScalarAsync();
        }

        // Undecodable bytes that pass the cheap checks: the lock is refused before anything is decoded.
        var error = await Assert.ThrowsAsync<AppProblemException>(() => service.Create(
            user.Id, Guid.NewGuid(), "UTC", null, [1, 2, 3], "image/png", CancellationToken.None));
        Assert.Equal(409, error.StatusCode);
        Assert.False(stub.Called);

        await using (var release = new NpgsqlCommand("select pg_advisory_unlock(hashtextextended(@key,0))", connection))
        {
            release.Parameters.AddWithValue("key", $"draft-extraction-user:{user.Id}");
            await release.ExecuteScalarAsync();
        }

        error = await Assert.ThrowsAsync<AppProblemException>(() => service.Create(
            user.Id, Guid.NewGuid(), "UTC", null, [1, 2, 3], "image/png", CancellationToken.None));
        Assert.Equal(400, error.StatusCode);
        Assert.Equal("Screenshot content does not match a valid supported image.", error.Message);
        Assert.False(stub.Called);
    }

    [Fact]
    public async Task ScreenshotIsNotDecodedOnceTheHourlyQuotaIsSpent()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var user = await CreateUser(db, "draft-quota");
        await using (var command = postgres.DataSource!.CreateCommand(
            "insert into draft_extraction_attempts(user_id,window_started_at,attempt_count) values (@user,now(),@limit)"))
        {
            command.Parameters.AddWithValue("user", user.Id);
            command.Parameters.AddWithValue("limit", new AiDraftOptions().RequestsPerHour);
            await command.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<AppProblemException>(() => CreateService(db, new StubExtractor([])).Create(
            user.Id, Guid.NewGuid(), "UTC", null, [1, 2, 3], "image/png", CancellationToken.None));
        Assert.Equal(429, error.StatusCode);
    }

    [Fact]
    public async Task UnsupportedScreenshotTypeIsRejectedBeforeTakingTheLock()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var user = await CreateUser(db, "draft-type");

        var error = await Assert.ThrowsAsync<AppProblemException>(() => CreateService(db, new StubExtractor([])).Create(
            user.Id, Guid.NewGuid(), "UTC", null, [1, 2, 3], "image/gif", CancellationToken.None));
        Assert.Equal(400, error.StatusCode);
        await using var attempts = postgres.DataSource!.CreateCommand(
            "select count(*) from draft_extraction_attempts where user_id = @user");
        attempts.Parameters.AddWithValue("user", user.Id);
        Assert.Equal(0L, await attempts.ExecuteScalarAsync());
    }

    private static async Task<User> CreateUser(PlaydexDbContext db, string prefix)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User { Email = $"{prefix}-{Guid.NewGuid():N}@test.local", CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private DraftExtractionService CreateService(PlaydexDbContext db, IPlaySessionExtractor extractor)
    {
        var igdbOptions = Options.Create(new IgdbOptions());
        var manualRepository = new ManualRepository(db);
        var draftRepository = new DraftRepository(db);
        var manual = new ManualService(
            manualRepository, new IgdbClient(new HttpClient(), igdbOptions), igdbOptions, new GameResolver(db), TestSessions.Lifecycle(db));
        return new DraftExtractionService(
            draftRepository,
            new DraftService(draftRepository, db, manual, manualRepository),
            manualRepository,
            extractor,
            Options.Create(new AiDraftOptions { Enabled = true, ApiKey = "test", Model = "test" }),
            postgres.DataSource!,
            TimeProvider.System,
            new GenerationHistoryRecorder(postgres.DataSource!, Options.Create(new AiDraftOptions { Provider = "test", Model = "test" }),
                TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<GenerationHistoryRecorder>.Instance));
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
        public AppProblemException? Error { get; set; }
        public bool Called { get; private set; }
        public Task<IReadOnlyList<ExtractedSession>> Extract(
            ExtractionInput input,
            CancellationToken cancellationToken)
        {
            Called = true;
            return Error is null ? Task.FromResult(rows) : Task.FromException<IReadOnlyList<ExtractedSession>>(Error);
        }
    }

    private PlaydexDbContext CreateDb() => new(new DbContextOptionsBuilder<PlaydexDbContext>()
        .UseNpgsql(postgres.DataSource!, options =>
        {
            options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            options.MapEnum<IntegrationType>("integration_type");
            options.MapEnum<CaptureMethod>("capture_method");
        }).Options);
}
