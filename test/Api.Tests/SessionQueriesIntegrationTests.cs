using Api.Modules.Accounts;
using Api.Modules.Games;
using Api.Modules.Recap;
using Microsoft.EntityFrameworkCore;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class SessionQueriesIntegrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task RecapQueriesSkipSessionsShorterThanTheIgnoreSetting()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var data = await Seed(db);
        var repository = new RecapRepository(db);

        Assert.Equal(
            new[] { 2025, 2026, data.Open.StartedAt.UtcDateTime.Year }.Distinct().OrderDescending(),
            await repository.ListRecapYears(data.UserId, TimeZoneInfo.Utc, CancellationToken.None));

        Assert.Equal(
            [data.Long.Id, data.Medium.Id, data.Open.Id],
            (await repository.ListRecapSessions(data.UserId, null, null, CancellationToken.None)).Select(row => row.Id));
        Assert.Equal(
            [data.Medium.Id],
            (await repository.ListRecapSessions(
                data.UserId,
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                CancellationToken.None)).Select(row => row.Id));

        var firstSessions = await repository.ListFirstSessionsByGame(data.UserId, CancellationToken.None);
        Assert.Equal([data.Alpha.Id, data.Beta.Id], firstSessions.Select(row => row.GameId));
        Assert.Equal(data.Long.StartedAt, firstSessions[0].FirstStartedAt);
        Assert.Equal(data.Medium.StartedAt, firstSessions[1].FirstStartedAt);
    }

    [Fact]
    public async Task AccountSessionQueriesFilterSortAndPage()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var data = await Seed(db);
        var repository = new AccountsRepository(db, new GamesService(new GamesRepository(db)));

        var longestFirst = await repository.ListSessions(
            data.UserId, null, null, null, null, null, null, "duration", "desc", 2, 0, 0, CancellationToken.None);
        Assert.Equal(4, longestFirst.Total);
        Assert.Equal([data.Open.Id, data.Long.Id], longestFirst.Sessions.Select(row => row.SessionId));

        var shortestFirst = await repository.ListSessions(
            data.UserId, null, null, null, null, null, null, "duration", "asc", 2, 0, 0, CancellationToken.None);
        Assert.Equal([data.Short.Id, data.Medium.Id], shortestFirst.Sessions.Select(row => row.SessionId));
        Assert.True(shortestFirst.Sessions[0].Ignored);
        Assert.False(shortestFirst.Sessions[1].Ignored);

        var byTitle = await repository.ListSessions(
            data.UserId, null, data.Beta.Title.ToUpperInvariant()[1..], null, null, null, null, "started", "asc", 10, 0, 0,
            CancellationToken.None);
        Assert.Equal([data.Medium.Id, data.Open.Id], byTitle.Sessions.Select(row => row.SessionId));

        var byGame = await repository.ListSessions(
            data.UserId, null, null, [data.Alpha.Id], null, null, null, "started", "asc", 10, 0, 0, CancellationToken.None);
        Assert.Equal([data.Short.Id, data.Long.Id], byGame.Sessions.Select(row => row.SessionId));

        var stats = await repository.ListSessionsForStats(data.UserId, null, null, null, 0, CancellationToken.None);
        Assert.Equal([data.Long.Id, data.Medium.Id, data.Open.Id], stats.Select(row => row.SessionId));

        Assert.Equal(data.Open.Id, (await repository.LoadCurrentSession(data.UserId, null, CancellationToken.None))?.SessionId);

        // The open session started 90 minutes ago: it runs into today even when it began yesterday.
        var today = await repository.ListSessionsToday(data.UserId, null, 0, TimeZoneInfo.Utc, CancellationToken.None);
        Assert.Equal([data.Open.Id], today.Select(row => row.SessionId));

        Assert.Equal(0, await repository.DeleteSession(data.Short.Id, data.UserId + 1, CancellationToken.None));
        Assert.Equal(data.Short.Id, await repository.DeleteSession(data.Short.Id, data.UserId, CancellationToken.None));
        Assert.False(await db.PlaySessions.AnyAsync(row => row.Id == data.Short.Id));
    }

    [Fact]
    public async Task AnOversizedIgnoreSettingCapsAtADayInsteadOfOverflowing()
    {
        if (!postgres.IsConfigured) return;

        await using var db = CreateDb();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var data = await Seed(db);
        // Stored before the setting was clamped: minutes * 60 overflows a 32-bit integer.
        await db.UserSettings
            .Where(settings => settings.UserId == data.UserId)
            .ExecuteUpdateAsync(set => set.SetProperty(settings => settings.IgnoreShortSessionsMinutes, int.MaxValue));

        var launcher = await new Api.Modules.Launcher.LauncherRepository(db).ListSessions(data.UserId, CancellationToken.None);
        Assert.Equal([data.Open.Id], launcher.Select(row => row.SessionId));

        var listed = await new AccountsRepository(db, new GamesService(new GamesRepository(db))).ListSessions(
            data.UserId, null, null, null, null, null, null, "date", "asc", 10, 0, 0, CancellationToken.None);
        Assert.Equal(4, listed.Total);
        Assert.Equal([true, true, true, false], listed.Sessions.Select(row => row.Ignored));
    }

    private static async Task<SeedData> Seed(PlaydexDbContext db)
    {
        var suffix = Guid.NewGuid().ToString("n");
        var user = new User { Email = $"session-queries-{suffix}@example.test" };
        var alpha = new Game { Title = $"Alpha {suffix}" };
        var beta = new Game { Title = $"Beta {suffix}" };
        var platform = new Platform { Key = $"session-queries-{suffix}", Name = "Test platform" };
        db.AddRange(user, alpha, beta, platform);
        await db.SaveChangesAsync();
        db.UserSettings.Add(new UserSettings { UserId = user.Id, IgnoreShortSessionsMinutes = 10 });

        PlaySession Session(Game game, DateTimeOffset startedAt, DateTimeOffset? endedAt) => new()
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = platform.Id,
            CaptureMethod = CaptureMethod.Manual,
            StartedAt = startedAt,
            EndedAt = endedAt,
        };

        var shortStart = new DateTimeOffset(2025, 2, 28, 10, 0, 0, TimeSpan.Zero);
        var longStart = new DateTimeOffset(2025, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var mediumStart = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero);
        var openStart = DateTimeOffset.UtcNow.AddMinutes(-90);
        var data = new SeedData(
            user.Id,
            alpha,
            beta,
            Short: Session(alpha, shortStart, shortStart.AddMinutes(5)),
            Long: Session(alpha, longStart, longStart.AddMinutes(60)),
            Medium: Session(beta, mediumStart, mediumStart.AddMinutes(30)),
            Open: Session(beta, openStart, null));
        db.PlaySessions.AddRange(data.Short, data.Long, data.Medium, data.Open);
        await db.SaveChangesAsync();
        return data;
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(postgres.DataSource!, options =>
            {
                options.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                options.MapEnum<IntegrationType>("integration_type");
                options.MapEnum<CaptureMethod>("capture_method");
            })
            .Options);

    private sealed record SeedData(
        long UserId,
        Game Alpha,
        Game Beta,
        PlaySession Short,
        PlaySession Long,
        PlaySession Medium,
        PlaySession Open);
}
