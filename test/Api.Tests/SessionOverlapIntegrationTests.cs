using Api.Infrastructure.Common;
using Api.Infrastructure.Http;
using Api.Modules.Accounts;
using Api.Modules.Games;
using Api.Modules.Manual;
using Api.Modules.Manual.Drafts;
using Api.Modules.Polling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

/// <summary>Manual entries, accepted drafts and edits do not overlap the user's other sessions on a platform.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class SessionOverlapIntegrationTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset _day = new(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EntriesOfOneDayFollowEachOtherAndADayWithoutRoomIsRefused()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var (user, game) = await Seed(db);
        var service = CreateManualService(db);
        async Task<long> Entry(int minutes, string platform) => (await service.CreateEntry(
                user.Id,
                new ManualEntryCreateRequest("2026-09-07", minutes, GameId.Encode(game.Id), null, game.Title, platform, null, "UTC"),
                CancellationToken.None)).SessionId;
        async Task<(DateTimeOffset, DateTimeOffset?)> Times(long id)
        {
            var session = await db.PlaySessions.AsNoTracking().SingleAsync(item => item.Id == id);
            return (session.StartedAt, session.EndedAt);
        }

        try
        {
            Assert.Equal((_day, _day.AddMinutes(90)), await Times(await Entry(90, "pc")));
            // The second entry of the day follows the first instead of starting at midnight too.
            Assert.Equal((_day.AddMinutes(90), _day.AddMinutes(150)), await Times(await Entry(60, "pc")));
            // Another platform has its own day.
            Assert.Equal((_day, _day.AddMinutes(60)), await Times(await Entry(60, "ps5")));

            // Play recorded on PC from 03:00 to 23:30 leaves 02:30-03:00 and 23:30-24:00.
            var pc = await db.Platforms.SingleAsync(item => item.Key == "pc");
            db.PlaySessions.Add(new PlaySession
            {
                UserId = user.Id,
                GameId = game.Id,
                PlatformId = pc.Id,
                CaptureMethod = CaptureMethod.Manual,
                StartedAt = _day.AddHours(3),
                EndedAt = _day.AddHours(23.5),
            });
            await db.SaveChangesAsync();

            var refused = await Assert.ThrowsAsync<AppProblemException>(() => Entry(120, "pc"));
            Assert.Equal(409, refused.StatusCode);
            Assert.Equal(
                "2026-09-07 has no 2 h free on PC: an entry that long would overlap the sessions already logged there.",
                refused.Message);

            Assert.Equal((_day.AddMinutes(150), _day.AddHours(3)), await Times(await Entry(30, "pc")));
            Assert.Equal((_day.AddHours(23.5), _day.AddDays(1)), await Times(await Entry(30, "pc")));
            Assert.Equal(409, (await Assert.ThrowsAsync<AppProblemException>(() => Entry(1, "pc"))).StatusCode);
        }
        finally
        {
            await Cleanup(db, user, game);
        }
    }

    [Fact]
    public async Task AcceptedDraftRowsOfOneDateFollowEachOther()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var (user, game) = await Seed(db);
        var now = DateTimeOffset.UtcNow;
        var batch = new SessionDraftBatch
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            RequestId = Guid.NewGuid(),
            CreatedAt = now,
            Timezone = "UTC",
            ReferenceDate = new DateOnly(2026, 9, 7),
        };
        SessionDraftRow Row(int position, int minutes) => new()
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            Position = position,
            Title = game.Title,
            Date = "2026-09-07",
            DurationMinutes = minutes,
            PlatformKey = "ps5",
            GameId = GameId.Encode(game.Id),
            SelectionJson = System.Text.Json.JsonSerializer.Serialize(
                new ManualMappingSearchResultDto("local", GameId.Encode(game.Id), null, game.Title, null, null, null)),
        };
        batch.Rows.Add(Row(0, 90));
        batch.Rows.Add(Row(1, 60));
        batch.Rows.Add(Row(2, 23 * 60));
        await new DraftRepository(db).CreateBatch(batch, default);
        var manual = CreateManualService(db);
        var drafts = new DraftService(new DraftRepository(db), db, manual, new ManualRepository(db));

        try
        {
            var results = (await drafts.Accept(
                user.Id, batch.Id, new DraftAcceptRequest(batch.Rows.Select(row => new DraftAcceptRow(row.Id, 1)).ToList()), default))
                .Results;

            var first = await db.PlaySessions.AsNoTracking().SingleAsync(item => item.Id == results[0].SessionId);
            var second = await db.PlaySessions.AsNoTracking().SingleAsync(item => item.Id == results[1].SessionId);
            Assert.Equal((_day, _day.AddMinutes(90)), (first.StartedAt, first.EndedAt));
            Assert.Equal((_day.AddMinutes(90), _day.AddMinutes(150)), (second.StartedAt, second.EndedAt));
            // 23 hours no longer fit next to the first two; the row stays pending with the reason.
            Assert.Null(results[2].SessionId);
            Assert.Equal(
                "2026-09-07 has no 23 h free on PS5: an entry that long would overlap the sessions already logged there.",
                results[2].Error);
            Assert.Equal(
                "pending",
                (await db.SessionDraftRows.AsNoTracking().SingleAsync(row => row.Id == batch.Rows[2].Id)).Status);
        }
        finally
        {
            await Cleanup(db, user, game);
        }
    }

    [Fact]
    public async Task EditsCannotOverlapAnotherSessionOnTheSamePlatform()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var (user, game) = await Seed(db);
        var other = new Game { Title = $"Control {Guid.NewGuid():n}" };
        db.Games.Add(other);
        await db.SaveChangesAsync();
        var pc = await db.Platforms.SingleAsync(item => item.Key == "pc");
        var ps5 = await db.Platforms.SingleAsync(item => item.Key == "ps5");
        PlaySession Add(Game played, Platform platform, double startHours, double? endHours)
        {
            var session = new PlaySession
            {
                UserId = user.Id,
                GameId = played.Id,
                PlatformId = platform.Id,
                CaptureMethod = CaptureMethod.Manual,
                StartedAt = _day.AddHours(startHours),
                EndedAt = endHours is { } end ? _day.AddHours(end) : null,
            };
            db.PlaySessions.Add(session);
            return session;
        }
        var morning = Add(other, pc, 10, 11);
        var noon = Add(game, pc, 12, 13);
        var console = Add(game, ps5, 10.5, 11.5);
        var running = Add(other, pc, 20, null);
        await db.SaveChangesAsync();
        var service = new AccountsService(new AccountsRepository(db, new GamesService(new GamesRepository(db))));
        Task<AccountSessionUpdateResponseDto> Edit(PlaySession session, double startHours, double endHours, int offsetHours = 0) =>
            service.UpdateSession(
                session.Id,
                user.Id,
                new AccountSessionUpdateRequest(
                    _day.AddHours(startHours).ToOffset(TimeSpan.FromHours(offsetHours)),
                    _day.AddHours(endHours).ToOffset(TimeSpan.FromHours(offsetHours))),
                CancellationToken.None);

        var overlap = await Assert.ThrowsAsync<AppProblemException>(() => Edit(noon, 10.5, 12.5, offsetHours: 2));
        Assert.Equal(409, overlap.StatusCode);
        Assert.Equal(
            $"This would overlap your {other.Title} session on PC (from 2026-09-07 12:00 to 13:00). "
                + "Sessions on the same platform cannot overlap.",
            overlap.Message);
        var unchanged = await db.PlaySessions.AsNoTracking().SingleAsync(item => item.Id == noon.Id);
        Assert.Equal(_day.AddHours(12), unchanged.StartedAt);

        // Back to back is fine, and so is moving within its own old times.
        Assert.Equal(_day.AddHours(11), (await Edit(noon, 11, 12.5)).StartedAt);
        Assert.Equal(_day.AddHours(11.5), (await Edit(noon, 11.5, 12)).StartedAt);
        // Another platform is another place to play.
        Assert.Equal(_day.AddHours(10), (await Edit(console, 10, 11)).StartedAt);

        var stillRunning = await Assert.ThrowsAsync<AppProblemException>(() => Edit(morning, 19, 21));
        Assert.Equal(409, stillRunning.StatusCode);
        Assert.Contains("(started 2026-09-07 20:00, still running)", stillRunning.Message);
        Assert.Equal(running.StartedAt, (await db.PlaySessions.AsNoTracking().SingleAsync(item => item.Id == running.Id)).StartedAt);
    }

    private static async Task<(User User, Game Game)> Seed(PlaydexDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"session-overlap-{suffix}@example.test" };
        var game = new Game { Title = $"Hades {suffix}" };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        return (user, game);
    }

    private static async Task Cleanup(PlaydexDbContext db, User user, Game game)
    {
        db.ChangeTracker.Clear();
        await db.PlaySessions.Where(item => item.UserId == user.Id).ExecuteDeleteAsync();
        await db.SessionDraftBatches.Where(item => item.UserId == user.Id).ExecuteDeleteAsync();
        await db.Users.Where(item => item.Id == user.Id).ExecuteDeleteAsync();
        await db.Games.Where(item => item.Id == game.Id).ExecuteDeleteAsync();
    }

    private static ManualService CreateManualService(PlaydexDbContext db) => new(
        new ManualRepository(db),
        new IgdbClient(new HttpClient(), Options.Create(new IgdbOptions()), new IgdbTokenCache()),
        Options.Create(new IgdbOptions()),
        new GameResolver(db),
        TestSessions.Lifecycle(db));

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            })
            .Options);
}
