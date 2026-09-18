using System.Diagnostics;
using System.Data.Common;
using System.Security.Claims;
using Admin.Features;
using Admin.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Playdex.Persistence;
using Xunit;

namespace Admin.Tests;

[Collection(AdminAuthenticationFixtureGroup.Name)]
public sealed class AdminOperationsIntegrationTests(AdminAuthenticationFixture fixture)
{
    [Fact]
    public async Task DashboardIncludesAllSourcesGlobalCountsCombinedFiltersAndBoundedRecents()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var factory = fixture.Factory.Services.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
        var operations = new DashboardOperations(factory, new AdminOperationGuard(factory), new FixedClock(now));
        var before = await operations.Read(scenario.Admin, "", null, now.Date, now.Date.AddDays(1));
        var polling = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, $"account-{scenario.Suffix}");
        var otherPlatform = await scenario.AddPlatformAsync($"other-{scenario.Suffix}");
        var otherUser = await scenario.AddUserAsync($"other-{scenario.Suffix}@test.local");
        var other = await scenario.AddIntegrationAsync(IntegrationType.Api, null, true, "api-account", userId: otherUser.Id);
        for (var i = 0; i < 4; i++) await scenario.AddSessionAsync(scenario.Game.Id, polling.Id, now.AddHours(-5).AddMinutes(i));
        var api = await scenario.AddSessionAsync(scenario.Game.Id, other.Id, now.AddHours(-1));
        var manual = await scenario.AddSessionAsync(scenario.Game.Id, polling.Id, now.AddHours(-2));
        await using var db = await scenario.CreateDbAsync();
        await db.PlaySessions.Where(r => r.Id == manual.Id).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.IntegrationId, (long?)null).SetProperty(r => r.CaptureMethod, CaptureMethod.Manual));
        await db.PlaySessions.Where(r => r.Id == api.Id).ExecuteUpdateAsync(set => set.SetProperty(r => r.CaptureMethod, CaptureMethod.Api).SetProperty(r => r.PlatformId, otherPlatform.Id));
        db.PollResults.AddRange(new PollResult { Id = scenario.NextEntityId(), IntegrationId = polling.Id, PolledAt = now.AddMinutes(-3), CreatedAt = now },
            new PollResult { Id = scenario.NextEntityId(), IntegrationId = polling.Id, PolledAt = now.AddMinutes(-1), CreatedAt = now });
        for (var i = 0; i < 12; i++)
        {
            await scenario.AddSessionAsync(scenario.Game.Id, polling.Id, now.AddDays(-2).AddMinutes(-i), now.AddMinutes(-i));
            db.AiGenerations.Add(new AiGeneration { Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), UserId = scenario.User.Id,
                CreatedAt = now.AddMinutes(-i), Provider = "test", Model = "test", Timezone = "UTC", Status = i % 2 == 0 ? "failure" : "timeout" });
        }
        db.AiGenerations.AddRange(new AiGeneration { Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), UserId = scenario.User.Id,
            CreatedAt = now.AddHours(-25), Provider = "test", Model = "test", Timezone = "UTC", Status = "cancelled" },
            new AiGeneration { Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), UserId = scenario.User.Id,
            CreatedAt = now, Provider = "test", Model = "test", Timezone = "UTC", Status = "success" });
        await db.SaveChangesAsync();
        var baseline = await operations.Read(scenario.Admin, "", null, now.Date, now.Date.AddDays(1));
        var all = baseline.LiveSessions.Where(r => r.Email == scenario.User.Email || r.Email == otherUser.Email).ToList();
        Assert.Equal(6, all.Count);
        Assert.Equal(all.OrderBy(r => r.StartedAt).Select(r => r.Id), all.Select(r => r.Id));
        Assert.Equal(now.AddMinutes(-1), all.First(r => r.Source == CaptureMethod.Polling).LastPolledAt);
        Assert.Null(all.Single(r => r.Source == CaptureMethod.Manual).LastPolledAt);
        Assert.Null(all.Single(r => r.Source == CaptureMethod.Api).LastPolledAt);
        var filtered = await operations.Read(scenario.Admin, scenario.Suffix.ToUpperInvariant(), scenario.Platform.Key, now.Date, now.Date.AddDays(1));
        Assert.Equal(baseline.Counts, filtered.Counts);
        Assert.Equal(5, filtered.LiveSessions.Count);
        Assert.All(filtered.LiveSessions, r => Assert.Equal(scenario.Platform.Key, r.PlatformKey));
        Assert.Equal(10, baseline.EndedSessions.Count);
        Assert.Equal(baseline.EndedSessions.OrderByDescending(r => r.EndedAt).Select(r => r.Id), baseline.EndedSessions.Select(r => r.Id));
        Assert.Equal(10, baseline.FailedGenerations.Count);
        Assert.Equal(before.Counts.LiveSessions + 6, baseline.Counts.LiveSessions);
        Assert.Equal(before.Counts.LiveUsers + 2, baseline.Counts.LiveUsers);
        Assert.Equal(before.Counts.SessionsToday + 6, baseline.Counts.SessionsToday);
        Assert.Equal(before.Counts.AiErrors + 12, baseline.Counts.AiErrors);
        Assert.All(baseline.FailedGenerations, r => Assert.True(r.Status is "failure" or "timeout" or "cancelled"));
        var localDayStart = now.AddHours(-14); // browser-local midnight at UTC-02h
        var localDayEnd = localDayStart.AddDays(1);
        var today = await scenario.Operations.SearchSessionsPageAsync(scenario.User.Email, 0, startedFrom: localDayStart, startedBefore: localDayEnd);
        Assert.Equal(5, today.Total);
        var errors = await new GenerationHistoryOperations(factory, new AdminOperationGuard(factory))
            .Search(scenario.Admin, scenario.User.Email, "errors", 0, since: now.AddHours(-24), until: now);
        Assert.Equal(12, errors.Total);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.Read(new ClaimsPrincipal(), "", null, now.Date, now.Date.AddDays(1)));
        var ordinary = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, scenario.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))], "test"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.Read(ordinary, "", null, now.Date, now.Date.AddDays(1)));
        await fixture.SetAdminAsync(false);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operations.Read(scenario.Admin, "", null, now.Date, now.Date.AddDays(1)));
        }
        finally { await fixture.SetAdminAsync(true); }
        await db.PollResults.Where(r => r.IntegrationId == polling.Id).ExecuteDeleteAsync();
        await db.AiGenerations.Where(r => r.UserId == scenario.User.Id).ExecuteDeleteAsync();
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => now; }

    private static readonly string[] OperationNames =
        ["set-platform-enabled", "update-integration", "transfer-sessions", "update-game", "merge-games"];

    [Fact]
    public async Task RecentSessionsAreLimitedAndDeterministicallyOrderedWithLiveDuration()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var started = DateTimeOffset.UtcNow.AddHours(-5);
        await scenario.AddSessionsAsync(4, started);

        var users = await scenario.Operations.GetRecentSessionsAsync();
        var user = Assert.Single(users, row => row.UserId == scenario.User.Id);

        Assert.Equal(3, user.Sessions.Count);
        Assert.Equal(scenario.SessionIds.OrderDescending().Take(3), user.Sessions.Select(row => row.Id));
        var live = Assert.Single(user.Sessions, row => row.Live);
        Assert.True(live.DurationSeconds >= (DateTimeOffset.UtcNow - live.StartedAt).TotalSeconds - 2);
    }

    [Fact]
    public async Task PlatformSummariesAndBulkStatusChangesValidateAndUpdateTimestamps()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "one");
        await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, false, "two");

        var summary = Assert.Single(await scenario.Operations.GetPlatformSummariesAsync(), row => row.Key == scenario.Platform.Key);
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Active);
        Assert.True(summary.Enabled);

        var changed = await scenario.Operations.SetPlatformEnabledAsync(scenario.Admin, scenario.Platform.Key, false);
        Assert.Equal(2, changed);
        await using var db = await scenario.CreateDbAsync();
        var integrations = await db.Integrations.AsNoTracking().Where(row => row.PlatformId == scenario.Platform.Id).ToListAsync();
        Assert.All(integrations, row => Assert.False(row.Active));
        Assert.All(integrations, row => Assert.True(row.UpdatedAt > Scenario.OldTimestamp));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            scenario.Operations.SetPlatformEnabledAsync(scenario.Admin, $"missing-{scenario.Suffix}", true));
    }

    [Fact]
    public async Task IntegrationListingAndPartialUpdatesPreserveUntouchedFields()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var platformIntegration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "z-account", "old-secret");
        var apiIntegration = await scenario.AddIntegrationAsync(IntegrationType.Api, null, true, "a-account", "api-secret");

        var rows = (await scenario.Operations.GetIntegrationsAsync(scenario.Admin)).Where(row => row.Email == scenario.User.Email).ToList();
        Assert.Equal("old-secret", Assert.Single(rows, row => row.Id == platformIntegration.Id).ApiKey);
        Assert.Equal("api-secret", Assert.Single(rows, row => row.Id == apiIntegration.Id).ApiKey);
        Assert.Equal(new[] { "api", scenario.Platform.Key }.Order(), rows.Select(row => row.Platform));

        Assert.True(await scenario.Operations.UpdateIntegrationAsync(
            scenario.Admin, platformIntegration.Id, false, "must-not-be-used", true, "  updated-account  "));
        await using var db = await scenario.CreateDbAsync();
        var updated = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == platformIntegration.Id);
        Assert.Equal("old-secret", updated.ApiKey);
        Assert.Equal("updated-account", updated.AccountId);
        Assert.True(updated.UpdatedAt > Scenario.OldTimestamp);

        Assert.True(await scenario.Operations.UpdateIntegrationAsync(
            scenario.Admin, apiIntegration.Id, true, "  replacement-secret  ", false, "must-not-be-used"));
        var api = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == apiIntegration.Id);
        Assert.Equal("replacement-secret", api.ApiKey);
        Assert.Equal("a-account", api.AccountId);
        Assert.False(await scenario.Operations.UpdateIntegrationAsync(scenario.Admin, long.MaxValue, true, null, false, null));
    }

    [Fact]
    public async Task SessionSearchPagesAndTransfersUseTargetOwnershipSemantics()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var source = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, $"account-{scenario.Suffix}");
        var targetUser = await scenario.AddUserAsync($"target-{scenario.Suffix}@example.test");
        var targetPlatform = await scenario.AddPlatformAsync($"target-{scenario.Suffix}");
        var target = await scenario.AddIntegrationAsync(IntegrationType.Psn, targetPlatform.Id, true, "target", userId: targetUser.Id);
        await scenario.AddSessionsAsync(27, DateTimeOffset.UtcNow.AddDays(-2), source.Id);

        var byEmail = await scenario.Operations.SearchSessionsPageAsync(scenario.User.Email, 0);
        Assert.Equal(27, byEmail.Total);
        Assert.Equal(25, byEmail.Sessions.Count);
        Assert.Equal(byEmail.Sessions.OrderByDescending(row => row.StartedAt).ThenByDescending(row => row.Id), byEmail.Sessions);
        Assert.Equal(2, (await scenario.Operations.SearchSessionsPageAsync(scenario.Game.Title, 1)).Sessions.Count);
        Assert.Equal(27, (await scenario.Operations.SearchSessionsPageAsync(source.AccountId!, 0)).Total);
        Assert.Equal(27, (await scenario.Operations.SearchSessionsPageAsync(scenario.Platform.Key, 0)).Total);
        Assert.Equal(1, (await scenario.Operations.SearchSessionsPageAsync(scenario.User.Email, 0, "live")).Total);
        Assert.Equal(26, (await scenario.Operations.SearchSessionsPageAsync(scenario.User.Email, 0, "ended")).Total);
        Assert.All((await scenario.Operations.SearchSessionsPageAsync(scenario.User.Email, 0, "live")).Sessions, row => Assert.True(row.Live));
        Assert.All((await scenario.Operations.SearchSessionsPageAsync(scenario.User.Email, 0, "ended")).Sessions, row => Assert.False(row.Live));

        var selected = byEmail.Sessions.Take(2).Select(row => row.Id).ToArray();
        Assert.Equal(2, selected.Distinct().Count());
        await using (var beforeTransfer = await scenario.CreateDbAsync())
        {
            var matchingRows = await beforeTransfer.PlaySessions.AsNoTracking().Where(row => selected.Contains(row.Id)).Select(row => new { row.Id, row.UserId, row.GameId }).ToListAsync();
            Assert.Equal(2, matchingRows.Count);
        }
        Assert.Equal(2, await scenario.Operations.TransferSessionsAsync(scenario.Admin, selected, target.Id));
        await using var db = await scenario.CreateDbAsync();
        var moved = await db.PlaySessions.AsNoTracking().Where(row => selected.Contains(row.Id)).ToListAsync();
        Assert.All(moved, row => Assert.Equal(target.Id, row.IntegrationId));
        Assert.All(moved, row => Assert.Equal(targetUser.Id, row.UserId));
        Assert.All(moved, row => Assert.Equal(targetPlatform.Id, row.PlatformId));

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Operations.TransferSessionsAsync(scenario.Admin, [], target.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scenario.Operations.TransferSessionsAsync(scenario.Admin, selected, long.MaxValue));
    }

    [Fact]
    public async Task GameSearchSupportsTextRawAndEncodedIdsAndEnforcesLimit()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        scenario.Game.Title = $"Needle {scenario.Suffix}";
        scenario.Game.CapsuleUrl = $"https://images.example/{scenario.Suffix}.jpg";
        await scenario.UpdateGameAsync();
        await scenario.AddGamesAsync(22, $"Needle {scenario.Suffix}");

        Assert.Contains(await scenario.Operations.SearchGamesAsync(scenario.Suffix), row => row.Id == scenario.Game.Id);
        Assert.Contains(await scenario.Operations.SearchGamesAsync(scenario.Game.CapsuleUrl), row => row.Id == scenario.Game.Id);
        Assert.Contains(await scenario.Operations.SearchGamesAsync(scenario.Game.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), row => row.Id == scenario.Game.Id);
        Assert.Contains(await scenario.Operations.SearchGamesAsync(EncodeGameId(scenario.Game.Id)), row => row.Id == scenario.Game.Id);
        Assert.Equal(20, (await scenario.Operations.SearchGamesAsync($"Needle {scenario.Suffix}")).Count);
        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Operations.SearchGamesAsync("  "));
    }

    [Fact]
    public async Task GameUpdatesTrimClearAndApplyExplicitOrAutomaticLocks()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);

        Assert.True(await scenario.Operations.UpdateGameAsync(
            scenario.Admin, scenario.Game.Id, "  New title  ", true, null, false, null, null));
        await using var db = await scenario.CreateDbAsync();
        var game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
        Assert.Equal("New title", game.Title);
        Assert.True(game.TitleLocked);

        Assert.True(await scenario.Operations.UpdateGameAsync(
            scenario.Admin, scenario.Game.Id, null, false, "  ", true, false, null));
        game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
        Assert.Null(game.CapsuleUrl);
        Assert.True(game.CapsuleUrlLocked);
        Assert.False(game.TitleLocked);

        Assert.True(await scenario.Operations.UpdateGameAsync(
            scenario.Admin, scenario.Game.Id, null, false, null, false, true, false));
        game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
        Assert.True(game.TitleLocked);
        Assert.False(game.CapsuleUrlLocked);
        Assert.True(game.UpdatedAt > Scenario.OldTimestamp);
        Assert.False(await scenario.Operations.UpdateGameAsync(scenario.Admin, long.MaxValue, "Missing", true, null, false, null, null));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Operations.UpdateGameAsync(scenario.Admin, scenario.Game.Id, "  ", true, null, false, null, null));
    }

    [Fact]
    public async Task GameMergeReassignsRelationshipsDeduplicatesAndDeletesSource()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var source = await scenario.AddGameAsync($"Source {scenario.Suffix}", true, "https://source/image.jpg");
        var neighbor = await scenario.AddGameAsync($"Neighbor {scenario.Suffix}");
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "merge");
        var session = await scenario.AddSessionAsync(source.Id, integration.Id);
        await using (var db = await scenario.CreateDbAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.GameExternalIdentities.Add(new GameExternalIdentity { Id = scenario.NextEntityId(), GameId = source.Id, Type = GameExternalIdentityType.Steam, ExternalId = $"merge-{scenario.Suffix}", Title = source.Title, CreatedAt = now, UpdatedAt = now });
            db.IgnoredGames.AddRange(
                new IgnoredGame { Id = scenario.NextEntityId(), UserId = scenario.User.Id, GameId = source.Id, CreatedAt = now, UpdatedAt = now },
                new IgnoredGame { Id = scenario.NextEntityId(), UserId = scenario.User.Id, GameId = scenario.Game.Id, CreatedAt = now, UpdatedAt = now });
            db.GameRatings.AddRange(
                new GameRating { Id = scenario.NextEntityId(), UserId = scenario.User.Id, GameId = source.Id, Rating = 4, CreatedAt = now, UpdatedAt = now },
                new GameRating { Id = scenario.NextEntityId(), UserId = scenario.User.Id, GameId = scenario.Game.Id, Rating = 5, CreatedAt = now, UpdatedAt = now });
            db.GameConnections.Add(new GameConnection
            {
                UserId = scenario.User.Id,
                GameId = Math.Min(source.Id, neighbor.Id),
                ConnectedGameId = Math.Max(source.Id, neighbor.Id),
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
            Assert.Single(await db.PlaySessions.AsNoTracking().Where(row => row.Id == session.Id).ToListAsync());
        }

        await scenario.Operations.MergeGamesAsync(scenario.Admin, source.Id, scenario.Game.Id);
        await using (var db = await scenario.CreateDbAsync())
        {
            Assert.False(await db.Games.AnyAsync(row => row.Id == source.Id));
            var target = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
            Assert.Equal(source.Title, target.Title);
            Assert.Equal(source.CapsuleUrl, target.CapsuleUrl);
            Assert.Equal(scenario.Game.Id, await db.GameExternalIdentities.Where(row => row.ExternalId == $"merge-{scenario.Suffix}").Select(row => row.GameId).SingleAsync());
            var mergedSessions = await db.PlaySessions.Where(row => row.Id == session.Id).Select(row => row.GameId).ToListAsync();
            Assert.Single(mergedSessions);
            Assert.Equal(scenario.Game.Id, mergedSessions[0]);
            Assert.Single(await db.IgnoredGames.Where(row => row.UserId == scenario.User.Id && row.GameId == scenario.Game.Id).ToListAsync());
            Assert.Single(await db.GameRatings.Where(row => row.UserId == scenario.User.Id && row.GameId == scenario.Game.Id).ToListAsync());
            Assert.Equal((short)5, await db.GameRatings.Where(row => row.UserId == scenario.User.Id && row.GameId == scenario.Game.Id).Select(row => row.Rating).SingleAsync());
            Assert.True(await db.GameConnections.AnyAsync(row => row.UserId == scenario.User.Id &&
                (row.GameId == scenario.Game.Id || row.ConnectedGameId == scenario.Game.Id) &&
                (row.GameId == neighbor.Id || row.ConnectedGameId == neighbor.Id)));
        }

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Operations.MergeGamesAsync(scenario.Admin, scenario.Game.Id, scenario.Game.Id));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => scenario.Operations.MergeGamesAsync(scenario.Admin, long.MaxValue, scenario.Game.Id));
    }

    [Fact]
    public async Task GameMergeRollsBackEveryEarlierChangeWhenRelationshipReassignmentFails()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var source = await scenario.AddGameAsync($"Rollback source {scenario.Suffix}", true);
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, false, "rollback");
        await scenario.AddSessionAsync(source.Id, integration.Id);
        await using (var db = await scenario.CreateDbAsync())
        {
            var now = DateTimeOffset.UtcNow;
            db.GameExternalIdentities.Add(new GameExternalIdentity
            {
                Id = scenario.NextEntityId(),
                GameId = source.Id,
                Type = GameExternalIdentityType.Psn,
                ExternalId = $"rollback-{scenario.Suffix}",
                Title = source.Title,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var dataSource = fixture.Factory.Services.GetRequiredService<NpgsqlDataSource>();
        var options = new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(dataSource)
            .AddInterceptors(new FailSessionReassignmentInterceptor())
            .Options;
        var failingFactory = new OptionsContextFactory(options);
        var operations = new AdminOperations(failingFactory, new AdminOperationGuard(failingFactory), new ListLogger());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operations.MergeGamesAsync(scenario.Admin, source.Id, scenario.Game.Id));

        await using var verification = await scenario.CreateDbAsync();
        Assert.True(await verification.Games.AnyAsync(row => row.Id == source.Id));
        Assert.Equal($"Target {scenario.Suffix}", await verification.Games.Where(row => row.Id == scenario.Game.Id).Select(row => row.Title).SingleAsync());
        Assert.Equal(source.Id, await verification.GameExternalIdentities
            .Where(row => row.ExternalId == $"rollback-{scenario.Suffix}")
            .Select(row => row.GameId)
            .SingleAsync());
    }

    [Fact]
    public async Task MutationsRevalidateAdministratorAndLogsDoNotContainSecrets()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "audit");
        var target = await scenario.AddIntegrationAsync(IntegrationType.Api, null, false, "audit-target");
        var session = await scenario.AddSessionAsync(scenario.Game.Id, integration.Id);
        var source = await scenario.AddGameAsync($"Audit source {scenario.Suffix}");
        using var activity = new Activity("admin-test").Start();
        const string secret = "super-secret-api-key";

        await scenario.Operations.SetPlatformEnabledAsync(scenario.Admin, scenario.Platform.Key, false);
        Assert.True(await scenario.Operations.UpdateIntegrationAsync(scenario.Admin, integration.Id, true, secret, false, null));
        await scenario.Operations.TransferSessionsAsync(scenario.Admin, [session.Id], target.Id);
        await scenario.Operations.UpdateGameAsync(scenario.Admin, scenario.Game.Id, "Audited target", true, null, false, null, null);
        await scenario.Operations.MergeGamesAsync(scenario.Admin, source.Id, scenario.Game.Id);
        var entry = Assert.Single(scenario.Logger.Entries, row => row.Contains("update-integration", StringComparison.Ordinal));
        Assert.Contains(activity.Id!, entry, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, entry, StringComparison.Ordinal);
        Assert.All(OperationNames, operation =>
            Assert.Single(scenario.Logger.Entries, row => row.Contains(operation, StringComparison.Ordinal)));
        Assert.All(scenario.Logger.Entries, row => Assert.Contains(activity.Id!, row, StringComparison.Ordinal));
        Assert.DoesNotContain(scenario.Logger.Entries, row => row.Contains(secret, StringComparison.Ordinal));

        var revoked = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            CookieAuthenticationDefaults.AuthenticationScheme));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Operations.SetPlatformEnabledAsync(revoked, scenario.Platform.Key, true));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Operations.UpdateIntegrationAsync(revoked, integration.Id, true, "not-written", false, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Operations.TransferSessionsAsync(revoked, [session.Id], target.Id));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Operations.UpdateGameAsync(revoked, scenario.Game.Id, "not-written", true, null, false, null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Operations.MergeGamesAsync(revoked, scenario.Game.Id, long.MaxValue));
    }

    [Fact]
    public async Task IntegrationApiKeysRequireCurrentAdministrator()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        const string secret = "integration-read-test-key";
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Api, null, true, "read-key", secret);
        var rows = await scenario.Operations.GetIntegrationsAsync(scenario.Admin);
        Assert.Equal(secret, Assert.Single(rows, row => row.Id == integration.Id).ApiKey);
        Assert.DoesNotContain(scenario.Logger.Entries, row => row.Contains(secret, StringComparison.Ordinal));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            scenario.Operations.GetIntegrationsAsync(new ClaimsPrincipal(new ClaimsIdentity())));
        var ordinaryUser = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, scenario.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new Claim("is_admin", "true")], CookieAuthenticationDefaults.AuthenticationScheme));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Operations.GetIntegrationsAsync(ordinaryUser));

        await fixture.SetAdminAsync(false);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Operations.GetIntegrationsAsync(scenario.Admin));
        }
        finally
        {
            await fixture.SetAdminAsync(true);
        }
    }

    private static string EncodeGameId(long id)
    {
        const string alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var value = (ulong)(id * 1299827 + 811);
        var result = "";
        while (value > 0) { result = alphabet[(int)(value % 62)] + result; value /= 62; }
        return result;
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly IDbContextFactory<PlaydexDbContext> factory;
        private readonly List<long> userIds = [];
        private readonly List<long> platformIds = [];
        private readonly List<long> gameIds = [];
        private long nextEntityId;
        private long nextSessionId = Random.Shared.NextInt64(8_000_000_000_000_000_000, 8_500_000_000_000_000_000);

        private Scenario(IDbContextFactory<PlaydexDbContext> factory, string suffix, User user, Platform platform, Game game, ClaimsPrincipal admin, ListLogger logger, long nextEntityId)
        {
            this.factory = factory;
            Suffix = suffix;
            User = user;
            Platform = platform;
            Game = game;
            Admin = admin;
            Logger = logger;
            this.nextEntityId = nextEntityId;
            userIds.Add(user.Id);
            platformIds.Add(platform.Id);
            gameIds.Add(game.Id);
            Operations = new AdminOperations(factory, new AdminOperationGuard(factory), logger);
        }

        public const string OldTimestampText = "2000-01-01T00:00:00+00:00";
        public static readonly DateTimeOffset OldTimestamp = DateTimeOffset.Parse(OldTimestampText, System.Globalization.CultureInfo.InvariantCulture);
        public string Suffix { get; }
        public User User { get; }
        public Platform Platform { get; }
        public Game Game { get; }
        public ClaimsPrincipal Admin { get; }
        public AdminOperations Operations { get; }
        public ListLogger Logger { get; }
        public List<long> SessionIds { get; } = [];

        public static async Task<Scenario> CreateAsync(AdminAuthenticationFixture fixture)
        {
            var scope = fixture.Factory!.Services.CreateScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PlaydexDbContext>>();
            scope.Dispose();
            var suffix = Guid.NewGuid().ToString("N")[..10];
            await using var db = await factory.CreateDbContextAsync();
            var entityBase = Random.Shared.NextInt64(7_000_000_000_000, 7_050_000_000_000);
            var user = new User { Id = entityBase, Email = $"operations-{suffix}@example.test", CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            var platform = new Platform { Id = entityBase + 1, Key = $"adm-{suffix}", Name = $"Admin {suffix}", CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            var game = new Game { Id = entityBase + 2, Title = $"Target {suffix}", CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.AddRange(user, platform, game);
            await db.SaveChangesAsync();
            var adminId = await fixture.GetAdminIdAsync();
            var admin = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, adminId.ToString(System.Globalization.CultureInfo.InvariantCulture))],
                CookieAuthenticationDefaults.AuthenticationScheme));
            return new Scenario(factory, suffix, user, platform, game, admin, new ListLogger(), entityBase + 3);
        }

        public Task<PlaydexDbContext> CreateDbAsync() => factory.CreateDbContextAsync();

        public async Task<User> AddUserAsync(string email)
        {
            await using var db = await CreateDbAsync();
            var user = new User { Id = NextEntityId(), Email = email, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userIds.Add(user.Id);
            return user;
        }

        public async Task<Platform> AddPlatformAsync(string key)
        {
            await using var db = await CreateDbAsync();
            var platform = new Platform { Id = NextEntityId(), Key = key, Name = key, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Platforms.Add(platform);
            await db.SaveChangesAsync();
            platformIds.Add(platform.Id);
            return platform;
        }

        public async Task<Integration> AddIntegrationAsync(IntegrationType type, long? platformId, bool active, string? accountId, string? apiKey = null, long? userId = null)
        {
            await using var db = await CreateDbAsync();
            var integration = new Integration { Id = NextEntityId(), UserId = userId ?? User.Id, Type = type, PlatformId = platformId, Active = active, AccountId = accountId, ApiKey = apiKey, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
            return integration;
        }

        public async Task<Game> AddGameAsync(string title, bool titleLocked = false, string? capsuleUrl = null)
        {
            await using var db = await CreateDbAsync();
            var game = new Game { Id = NextEntityId(), Title = title, TitleLocked = titleLocked, CapsuleUrl = capsuleUrl, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            gameIds.Add(game.Id);
            return game;
        }

        public async Task AddGamesAsync(int count, string titlePrefix)
        {
            await using var db = await CreateDbAsync();
            var games = Enumerable.Range(0, count).Select(index => new Game { Id = NextEntityId(), Title = $"{titlePrefix} {index:D2}", CreatedAt = OldTimestamp.AddMinutes(index), UpdatedAt = OldTimestamp.AddMinutes(index) }).ToList();
            db.Games.AddRange(games);
            await db.SaveChangesAsync();
            gameIds.AddRange(games.Select(row => row.Id));
        }

        public async Task UpdateGameAsync()
        {
            await using var db = await CreateDbAsync();
            var updated = await db.Games.Where(row => row.Id == Game.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Title, Game.Title)
                .SetProperty(row => row.CapsuleUrl, Game.CapsuleUrl)
                .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow));
            Assert.Equal(1, updated);
        }

        public async Task AddSessionsAsync(int count, DateTimeOffset started, long? integrationId = null)
        {
            if (!integrationId.HasValue)
            {
                integrationId = (await AddIntegrationAsync(IntegrationType.Steam, Platform.Id, true, $"sessions-{Suffix}")).Id;
            }
            for (var index = 0; index < count; index++)
            {
                await AddSessionAsync(Game.Id, integrationId.Value, started.AddMinutes(index), index == count - 1 ? null : started.AddMinutes(index + 1));
            }
        }

        public async Task<PlaySession> AddSessionAsync(long gameId, long integrationId, DateTimeOffset? started = null, DateTimeOffset? ended = null)
        {
            await using var db = await CreateDbAsync();
            var integration = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == integrationId);
            var session = new PlaySession
            {
                Id = nextSessionId++,
                GameId = gameId,
                UserId = integration.UserId,
                PlatformId = integration.PlatformId ?? Platform.Id,
                IntegrationId = integration.Id,
                CaptureMethod = CaptureMethod.Polling,
                StartedAt = started ?? DateTimeOffset.UtcNow.AddHours(-1),
                EndedAt = ended,
                CreatedAt = OldTimestamp,
                UpdatedAt = OldTimestamp,
            };
            db.PlaySessions.Add(session);
            await db.SaveChangesAsync();
            Assert.True(session.Id > 0);
            SessionIds.Add(session.Id);
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = await CreateDbAsync();
            await db.GameConnections.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.IgnoredGames.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.GameRatings.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.PlaySessions.Where(row => userIds.Contains(row.UserId) || gameIds.Contains(row.GameId)).ExecuteDeleteAsync();
            await db.GameExternalIdentities.Where(row => gameIds.Contains(row.GameId)).ExecuteDeleteAsync();
            await db.Integrations.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.Games.Where(row => gameIds.Contains(row.Id)).ExecuteDeleteAsync();
            await db.Platforms.Where(row => platformIds.Contains(row.Id)).ExecuteDeleteAsync();
            await db.Users.Where(row => userIds.Contains(row.Id)).ExecuteDeleteAsync();
        }

        public long NextEntityId() => nextEntityId++;
    }

    public sealed class ListLogger : ILogger<AdminOperations>
    {
        public List<string> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
    }

    private sealed class OptionsContextFactory(DbContextOptions<PlaydexDbContext> options) : IDbContextFactory<PlaydexDbContext>
    {
        public PlaydexDbContext CreateDbContext() => new(options);
        public Task<PlaydexDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }

    private sealed class FailSessionReassignmentInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("update play_sessions set game_id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Injected relationship reassignment failure.");
            }

            return ValueTask.FromResult(result);
        }
    }
}
