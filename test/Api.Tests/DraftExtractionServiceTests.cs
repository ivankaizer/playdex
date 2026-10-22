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
            new IgdbClient(new HttpClient(), igdbOptions, new IgdbTokenCache()),
            igdbOptions,
            new GameResolver(db));
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
    public async Task GameSearchThatRunsOutOfTimeStillSavesTheExtractedRows()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var user = new User { Email = $"draft-slow-search-{Guid.NewGuid():N}@test.local", CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var title = $"Unmatched title {Guid.NewGuid():N}";
        var service = CreateService(
            db,
            new HangingHandler(),
            new StubExtractor([new(title, "2026-09-09", 30, "pc", []), new(title, "2026-09-10", 45, "pc", [])]),
            new AiDraftOptions { Enabled = true, ApiKey = "test", Model = "test", TimeoutSeconds = 1, SearchTimeoutSeconds = 1 });

        var batch = await service.Create(user.Id, Guid.NewGuid(), "UTC", "Two sessions", null, null, CancellationToken.None);

        Assert.Equal(2, batch.Rows.Count);
        Assert.All(batch.Rows, row => Assert.Null(row.Selection));
        Assert.Equal("success", (await db.AiGenerations.AsNoTracking().SingleAsync(row => row.UserId == user.Id)).Status);
    }

    [Fact]
    public async Task IgdbResultsAlreadySavedAsGamesAreOfferedAsThoseGames()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        // IGDB ids are ints (IgdbClient reads them as such); a larger one fails the search and leaves no results.
        var igdbId = Random.Shared.Next(1_000_000_000, int.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var user = new User { Email = $"draft-saved-{Guid.NewGuid():N}@test.local", CreatedAt = now, UpdatedAt = now };
        var saved = new Game { Title = $"Saved {igdbId}", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, saved);
        await db.SaveChangesAsync();
        db.GameExternalIdentities.Add(new GameExternalIdentity
        {
            GameId = saved.Id, Type = GameExternalIdentityType.Igdb, ExternalId = igdbId, Title = saved.Title,
            CreatedAt = now, UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        var igdb = new IgdbHandler($$"""[{"id":{{igdbId}},"name":"Saved"},{"id":1,"name":"Not saved"}]""");

        var candidates = await CreateDraftService(db, igdb)
            .SearchCandidates(user.Id, ["Zzq first", "Zzq second", "Zzq first"], TimeSpan.FromSeconds(30), default);

        Assert.Equal(["Zzq first", "Zzq second"], candidates.Keys.Order());
        Assert.Equal(2, igdb.Searches);
        Assert.All(candidates.Values, results =>
        {
            Assert.Contains(results, row => row.Source == "local" && row.GameId == Api.Infrastructure.Common.GameId.Encode(saved.Id));
            Assert.Contains(results, row => row.Source == "igdb" && row.IgdbId == "1");
            Assert.DoesNotContain(results, row => row.IgdbId == igdbId);
        });
    }

    private DraftExtractionService CreateService(PlaydexDbContext db, HttpMessageHandler igdb, IPlaySessionExtractor extractor,
        AiDraftOptions options)
    {
        var draftRepository = new DraftRepository(db);
        var manualRepository = new ManualRepository(db);
        return new DraftExtractionService(draftRepository, CreateDraftService(db, igdb), manualRepository, extractor,
            Options.Create(options), postgres.DataSource!, TimeProvider.System,
            new GenerationHistoryRecorder(postgres.DataSource!, Options.Create(new AiDraftOptions { Provider = "test", Model = "test" }),
                TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<GenerationHistoryRecorder>.Instance));
    }

    private static DraftService CreateDraftService(PlaydexDbContext db, HttpMessageHandler igdb)
    {
        var igdbOptions = Options.Create(new IgdbOptions { ClientId = "client", ClientSecret = "secret" });
        var manualRepository = new ManualRepository(db);
        var manual = new ManualService(manualRepository,
            new IgdbClient(new HttpClient(igdb), igdbOptions, new IgdbTokenCache()), igdbOptions, new GameResolver(db));
        return new DraftService(new DraftRepository(db), db, manual, manualRepository);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    private sealed class IgdbHandler(string games) : HttpMessageHandler
    {
        private int _searches;
        public int Searches => _searches;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.RequestUri!.Host == "id.twitch.tv"
                ? """{"access_token":"token","expires_in":3600}"""
                : games;
            if (request.RequestUri.Host != "id.twitch.tv") Interlocked.Increment(ref _searches);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
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
        public Task<IReadOnlyList<ExtractedSession>> Extract(
            ExtractionInput input,
            CancellationToken cancellationToken) => Error is null ? Task.FromResult(rows) : Task.FromException<IReadOnlyList<ExtractedSession>>(Error);
    }

    private PlaydexDbContext CreateDb() => new(new DbContextOptionsBuilder<PlaydexDbContext>()
        .UseNpgsql(postgres.DataSource!, options =>
        {
            options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            options.MapEnum<IntegrationType>("integration_type");
            options.MapEnum<CaptureMethod>("capture_method");
        }).Options);
}
