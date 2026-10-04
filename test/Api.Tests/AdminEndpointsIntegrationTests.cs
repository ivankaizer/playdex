using System.Data.Common;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Api.Infrastructure.Common;
using Api.Modules.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Playdex.Persistence;
using Xunit;

namespace Api.Tests;

[Collection(AdminApiCollectionDefinition.Name)]
public sealed class AdminEndpointsIntegrationTests(AdminApiFixture fixture)
{
    [Fact]
    public async Task DashboardIncludesAllSourcesGlobalCountsCombinedFiltersAndBoundedRecents()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var now = DateTimeOffset.UtcNow;
        var dayStart = now.AddHours(-6);
        var dayEnd = dayStart.AddDays(1);
        var before = await Dashboard(client, "", null, dayStart, dayEnd);
        var polling = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, $"account-{scenario.Suffix}");
        var otherPlatform = await scenario.AddPlatformAsync($"other-{scenario.Suffix}");
        var otherUser = await scenario.AddUserAsync($"other-{scenario.Suffix}@test.local");
        var other = await scenario.AddIntegrationAsync(IntegrationType.Api, null, true, "api-account", userId: otherUser.Id);
        for (var i = 0; i < 4; i++) await scenario.AddSessionAsync(scenario.Game.Id, polling.Id, now.AddHours(-5).AddMinutes(i));
        var api = await scenario.AddSessionAsync(scenario.Game.Id, other.Id, now.AddHours(-1));
        var manual = await scenario.AddSessionAsync(scenario.Game.Id, polling.Id, now.AddHours(-2));
        await using var db = fixture.CreateDb();
        await db.PlaySessions.Where(r => r.Id == manual.Id).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.IntegrationId, (long?)null).SetProperty(r => r.CaptureMethod, CaptureMethod.Manual));
        await db.PlaySessions.Where(r => r.Id == api.Id).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.CaptureMethod, CaptureMethod.Api).SetProperty(r => r.PlatformId, otherPlatform.Id));
        db.PollResults.AddRange(
            new PollResult { Id = scenario.NextEntityId(), IntegrationId = polling.Id, PolledAt = now.AddMinutes(-3), CreatedAt = now },
            new PollResult { Id = scenario.NextEntityId(), IntegrationId = polling.Id, PolledAt = now.AddMinutes(-1), CreatedAt = now });
        for (var i = 0; i < 12; i++)
        {
            await scenario.AddSessionAsync(scenario.Game.Id, polling.Id, now.AddDays(-2).AddMinutes(-i), now.AddMinutes(-i));
            db.AiGenerations.Add(new AiGeneration
            {
                Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), UserId = scenario.User.Id,
                CreatedAt = now.AddMinutes(-i - 1), Provider = "test", Model = "test", Timezone = "UTC",
                Status = i % 2 == 0 ? "failure" : "timeout",
            });
        }

        db.AiGenerations.AddRange(
            new AiGeneration { Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), UserId = scenario.User.Id,
                CreatedAt = now.AddHours(-25), Provider = "test", Model = "test", Timezone = "UTC", Status = "cancelled" },
            new AiGeneration { Id = Guid.NewGuid(), RequestId = Guid.NewGuid(), UserId = scenario.User.Id,
                CreatedAt = now.AddMinutes(-1), Provider = "test", Model = "test", Timezone = "UTC", Status = "success" });
        await db.SaveChangesAsync();
        try
        {
            var baseline = await Dashboard(client, "", null, dayStart, dayEnd);
            var all = baseline.LiveSessions.Where(r => r.Email == scenario.User.Email || r.Email == otherUser.Email).ToList();
            Assert.Equal(6, all.Count);
            Assert.Equal(all.OrderBy(r => r.StartedAt).Select(r => r.Id), all.Select(r => r.Id));
            Assert.Equal(now.AddMinutes(-1).ToUnixTimeMilliseconds(),
                all.First(r => r.Source == "polling").LastPolledAt?.ToUnixTimeMilliseconds());
            Assert.Null(all.Single(r => r.Source == "manual").LastPolledAt);
            Assert.Null(all.Single(r => r.Source == "api").LastPolledAt);
            var filtered = await Dashboard(client, scenario.Suffix.ToUpperInvariant(), scenario.Platform.Key, dayStart, dayEnd);
            Assert.Equal(baseline.Counts, filtered.Counts);
            Assert.Equal(5, filtered.LiveSessions.Count);
            Assert.All(filtered.LiveSessions, r => Assert.Equal(scenario.Platform.Key, r.PlatformKey));
            Assert.Equal(10, baseline.EndedSessions.Count);
            Assert.Equal(baseline.EndedSessions.OrderByDescending(r => r.EndedAt).Select(r => r.Id),
                baseline.EndedSessions.Select(r => r.Id));
            Assert.Equal(10, baseline.FailedGenerations.Count);
            Assert.Equal(before.Counts.LiveSessions + 6, baseline.Counts.LiveSessions);
            Assert.Equal(before.Counts.LiveUsers + 2, baseline.Counts.LiveUsers);
            Assert.Equal(before.Counts.SessionsToday + 6, baseline.Counts.SessionsToday);
            Assert.Equal(before.Counts.AiErrors + 12, baseline.Counts.AiErrors);
            Assert.All(baseline.FailedGenerations, r => Assert.True(r.Status is "failure" or "timeout" or "cancelled"));
            Assert.Contains(baseline.Platforms, row => row.Key == scenario.Platform.Key);

            var today = await AdminApiFixture.ReadDataAsync<AdminSessionPageDto>(await client.GetAsync(
                $"/api/admin/sessions?query={Uri.EscapeDataString(scenario.User.Email)}&from={Iso(dayStart)}&to={Iso(dayEnd)}"));
            Assert.Equal(5, today.Total);
            var errors = await AdminApiFixture.ReadDataAsync<AdminGenerationPageDto>(await client.GetAsync(
                $"/api/admin/ai-generations?query={Uri.EscapeDataString(scenario.User.Email)}&status=errors&since={Iso(now.AddHours(-24))}&until={Iso(now)}"));
            Assert.Equal(12, errors.Total);
        }
        finally
        {
            await db.PollResults.Where(r => r.IntegrationId == polling.Id).ExecuteDeleteAsync();
            await db.AiGenerations.Where(r => r.UserId == scenario.User.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task PlatformSummariesAndBulkStatusChangesValidateAndUpdateTimestamps()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "one");
        await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, false, "two");

        var summaries = await AdminApiFixture.ReadDataAsync<List<PlatformSummary>>(await client.GetAsync("/api/admin/platforms"));
        var summary = Assert.Single(summaries, row => row.Key == scenario.Platform.Key);
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Active);
        Assert.True(summary.Enabled);

        var changed = await AdminApiFixture.ReadDataAsync<AdminPlatformUpdateResultDto>(
            await client.PutAsJsonAsync($"/api/admin/platforms/{scenario.Platform.Key}", new { enabled = false }));
        Assert.Equal(2, changed.Updated);
        await using var db = fixture.CreateDb();
        var integrations = await db.Integrations.AsNoTracking().Where(row => row.PlatformId == scenario.Platform.Id).ToListAsync();
        Assert.All(integrations, row => Assert.False(row.Active));
        Assert.All(integrations, row => Assert.True(row.UpdatedAt > Scenario.OldTimestamp));

        using var missing = await client.PutAsJsonAsync($"/api/admin/platforms/missing-{scenario.Suffix}", new { enabled = true });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Platform not found.", await AdminApiFixture.ReadErrorAsync(missing));
        using var invalid = await client.PutAsJsonAsync($"/api/admin/platforms/{scenario.Platform.Key}", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task IntegrationListingAndPartialUpdatesPreserveUntouchedFields()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var platformIntegration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "z-account", "old-secret");
        var apiIntegration = await scenario.AddIntegrationAsync(IntegrationType.Api, null, true, "a-account", "api-secret");

        var rows = (await AdminApiFixture.ReadDataAsync<List<AdminIntegrationDto>>(await client.GetAsync("/api/admin/integrations")))
            .Where(row => row.Email == scenario.User.Email).ToList();
        Assert.Equal("old-secret", Assert.Single(rows, row => row.Id == platformIntegration.Id).ApiKey);
        Assert.Equal("api-secret", Assert.Single(rows, row => row.Id == apiIntegration.Id).ApiKey);
        Assert.Equal(new[] { "api", scenario.Platform.Key }.Order(), rows.Select(row => row.Platform));

        using (var response = await client.PatchAsJsonAsync($"/api/admin/integrations/{platformIntegration.Id}",
                   new { apiKeyChanged = false, apiKey = "must-not-be-used", accountIdChanged = true, accountId = "  updated-account  " }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await using var db = fixture.CreateDb();
        var updated = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == platformIntegration.Id);
        Assert.Equal("old-secret", updated.ApiKey);
        Assert.Equal("updated-account", updated.AccountId);
        Assert.True(updated.UpdatedAt > Scenario.OldTimestamp);

        using (var response = await client.PatchAsJsonAsync($"/api/admin/integrations/{apiIntegration.Id}",
                   new { apiKeyChanged = true, apiKey = "  replacement-secret  ", accountIdChanged = false, accountId = "must-not-be-used" }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var api = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == apiIntegration.Id);
        Assert.Equal("replacement-secret", api.ApiKey);
        Assert.Equal("a-account", api.AccountId);

        using (var response = await client.PatchAsJsonAsync($"/api/admin/integrations/{apiIntegration.Id}",
                   new { apiKeyChanged = true, apiKey = "   ", accountIdChanged = false }))
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null((await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == apiIntegration.Id)).ApiKey);

        using var missing = await client.PatchAsJsonAsync($"/api/admin/integrations/{long.MaxValue}",
            new { apiKeyChanged = true, apiKey = (string?)null, accountIdChanged = false });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Integration not found.", await AdminApiFixture.ReadErrorAsync(missing));
        using var nothing = await client.PatchAsJsonAsync($"/api/admin/integrations/{apiIntegration.Id}",
            new { apiKeyChanged = false, accountIdChanged = false });
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
        Assert.Equal("At least one credential field must be selected.", await AdminApiFixture.ReadErrorAsync(nothing));
    }

    [Fact]
    public async Task SessionSearchPagesAndTransfersUseTargetOwnershipSemantics()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var source = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, $"account-{scenario.Suffix}");
        var targetUser = await scenario.AddUserAsync($"target-{scenario.Suffix}@example.test");
        var targetPlatform = await scenario.AddPlatformAsync($"target-{scenario.Suffix}");
        var target = await scenario.AddIntegrationAsync(IntegrationType.Psn, targetPlatform.Id, true, "target", userId: targetUser.Id);
        await scenario.AddSessionsAsync(27, DateTimeOffset.UtcNow.AddDays(-2), source.Id);

        var byEmail = await Sessions(client, scenario.User.Email, 0);
        Assert.Equal(27, byEmail.Total);
        Assert.Equal(25, byEmail.Sessions.Count);
        Assert.Equal(0, byEmail.Page);
        Assert.Equal(25, byEmail.PageSize);
        Assert.Equal(byEmail.Sessions.OrderByDescending(row => row.StartedAt).ThenByDescending(row => row.Id), byEmail.Sessions);
        var second = await Sessions(client, scenario.Game.Title, 1);
        Assert.Equal(2, second.Sessions.Count);
        Assert.Equal(1, second.Page);
        Assert.Equal(27, (await Sessions(client, source.AccountId!, 0)).Total);
        Assert.Equal(27, (await Sessions(client, scenario.Platform.Key, 0)).Total);
        var live = await Sessions(client, scenario.User.Email, 0, "live");
        Assert.Equal(1, live.Total);
        Assert.All(live.Sessions, row => Assert.True(row.Live));
        var ended = await Sessions(client, scenario.User.Email, 0, "ended");
        Assert.Equal(26, ended.Total);
        Assert.All(ended.Sessions, row => Assert.False(row.Live));

        var selected = byEmail.Sessions.Take(2).Select(row => row.Id).ToArray();
        var transferred = await AdminApiFixture.ReadDataAsync<AdminTransferSessionsResultDto>(await client.PostAsJsonAsync(
            "/api/admin/sessions/transfer", new { sessionIds = selected, targetIntegrationId = target.Id }));
        Assert.Equal(2, transferred.Transferred);
        await using var db = fixture.CreateDb();
        var moved = await db.PlaySessions.AsNoTracking().Where(row => selected.Contains(row.Id)).ToListAsync();
        Assert.All(moved, row => Assert.Equal(target.Id, row.IntegrationId));
        Assert.All(moved, row => Assert.Equal(targetUser.Id, row.UserId));
        Assert.All(moved, row => Assert.Equal(targetPlatform.Id, row.PlatformId));
        Assert.All(moved, row => Assert.Equal(CaptureMethod.Polling, row.CaptureMethod));

        using (var empty = await client.PostAsJsonAsync("/api/admin/sessions/transfer", new { sessionIds = Array.Empty<long>(), targetIntegrationId = target.Id }))
        {
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
            Assert.Equal("At least one session is required.", await AdminApiFixture.ReadErrorAsync(empty));
        }

        using (var missing = await client.PostAsJsonAsync("/api/admin/sessions/transfer", new { sessionIds = selected, targetIntegrationId = long.MaxValue }))
        {
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("Target integration not found.", await AdminApiFixture.ReadErrorAsync(missing));
        }
    }

    [Fact]
    public async Task TransferKeepsCaptureMethodConsistentAndRejectsManualSessions()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var polling = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "polling");
        var apiTarget = await scenario.AddIntegrationAsync(IntegrationType.Api, null, true, "api-target");
        var pollingTarget = await scenario.AddIntegrationAsync(IntegrationType.Xbox, scenario.Platform.Id, true, "xbox-target");
        var session = await scenario.AddSessionAsync(scenario.Game.Id, polling.Id);
        var manual = await scenario.AddSessionAsync(scenario.Game.Id, polling.Id);
        await using var db = fixture.CreateDb();
        await db.PlaySessions.Where(r => r.Id == manual.Id).ExecuteUpdateAsync(set => set
            .SetProperty(r => r.IntegrationId, (long?)null).SetProperty(r => r.CaptureMethod, CaptureMethod.Manual));

        // Polling → API integration: the session becomes an API capture and keeps its platform.
        await Transfer(client, [session.Id], apiTarget.Id, expected: 1);
        var row = await db.PlaySessions.AsNoTracking().SingleAsync(r => r.Id == session.Id);
        Assert.Equal((apiTarget.Id, CaptureMethod.Api, scenario.Platform.Id), (row.IntegrationId!.Value, row.CaptureMethod, row.PlatformId));

        // API → polling integration: the session becomes a polling capture again.
        await Transfer(client, [session.Id], pollingTarget.Id, expected: 1);
        row = await db.PlaySessions.AsNoTracking().SingleAsync(r => r.Id == session.Id);
        Assert.Equal((pollingTarget.Id, CaptureMethod.Polling), (row.IntegrationId!.Value, row.CaptureMethod));

        // A selection containing a manual session is rejected as a whole and nothing changes.
        using var rejected = await client.PostAsJsonAsync("/api/admin/sessions/transfer",
            new { sessionIds = new[] { session.Id, manual.Id }, targetIntegrationId = apiTarget.Id });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("Manual sessions", await AdminApiFixture.ReadErrorAsync(rejected), StringComparison.Ordinal);
        Assert.Equal(pollingTarget.Id, (await db.PlaySessions.AsNoTracking().SingleAsync(r => r.Id == session.Id)).IntegrationId);
        var untouched = await db.PlaySessions.AsNoTracking().SingleAsync(r => r.Id == manual.Id);
        Assert.Equal((CaptureMethod.Manual, (long?)null), (untouched.CaptureMethod, untouched.IntegrationId));
    }

    [Fact]
    public async Task GameSearchSupportsTextRawAndEncodedIdsAndEnforcesLimit()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        scenario.Game.Title = $"Needle {scenario.Suffix}";
        scenario.Game.CapsuleUrl = $"https://images.example/{scenario.Suffix}.jpg";
        await scenario.UpdateGameAsync();
        await scenario.AddGamesAsync(22, $"Needle {scenario.Suffix}");

        Assert.Contains(await Games(client, scenario.Suffix), row => row.Id == scenario.Game.Id);
        Assert.Contains(await Games(client, scenario.Game.CapsuleUrl), row => row.Id == scenario.Game.Id);
        Assert.Contains(await Games(client, scenario.Game.Id.ToString(CultureInfo.InvariantCulture)), row => row.Id == scenario.Game.Id);
        Assert.Contains(await Games(client, GameId.Encode(scenario.Game.Id)), row => row.Id == scenario.Game.Id);
        Assert.Equal(20, (await Games(client, $"Needle {scenario.Suffix}")).Count);
        // An empty query lists the most recently updated games (the default view).
        var recent = await Games(client, "  ");
        Assert.Equal(20, recent.Count);
        Assert.Contains(recent, row => row.Id == scenario.Game.Id);
    }

    [Fact]
    public async Task GameUpdatesTrimClearAndApplyExplicitOrAutomaticLocks()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var path = $"/api/admin/games/{scenario.Game.Id}";

        await Patch(client, path, new { title = "  New title  ", titleChanged = true, capsuleUrlChanged = false });
        await using var db = fixture.CreateDb();
        var game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
        Assert.Equal("New title", game.Title);
        Assert.True(game.TitleLocked);

        await Patch(client, path, new { titleChanged = false, capsuleUrl = "  ", capsuleUrlChanged = true, titleLocked = false });
        game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
        Assert.Null(game.CapsuleUrl);
        Assert.True(game.CapsuleUrlLocked);
        Assert.False(game.TitleLocked);

        await Patch(client, path, new { titleChanged = false, capsuleUrlChanged = false, titleLocked = true, capsuleUrlLocked = false });
        game = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
        Assert.True(game.TitleLocked);
        Assert.False(game.CapsuleUrlLocked);
        Assert.True(game.UpdatedAt > Scenario.OldTimestamp);

        using var missing = await client.PatchAsJsonAsync($"/api/admin/games/{long.MaxValue}", new { title = "Missing", titleChanged = true });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("Game not found.", await AdminApiFixture.ReadErrorAsync(missing));
        using var blank = await client.PatchAsJsonAsync(path, new { title = "  ", titleChanged = true });
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("Title is required.", await AdminApiFixture.ReadErrorAsync(blank));
        using var nothing = await client.PatchAsJsonAsync(path, new { titleChanged = false, capsuleUrlChanged = false });
        Assert.Equal(HttpStatusCode.BadRequest, nothing.StatusCode);
    }

    [Fact]
    public async Task GameMergeReassignsRelationshipsDraftRowsDeduplicatesAndDeletesSource()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var source = await scenario.AddGameAsync($"Source {scenario.Suffix}", true, "https://source/image.jpg");
        var neighbor = await scenario.AddGameAsync($"Neighbor {scenario.Suffix}");
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "merge");
        var session = await scenario.AddSessionAsync(source.Id, integration.Id);
        var batchId = Guid.NewGuid();
        var draftId = Guid.NewGuid();
        var otherDraftId = Guid.NewGuid();
        await using (var db = fixture.CreateDb())
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
            db.SessionDraftBatches.Add(new SessionDraftBatch
            {
                Id = batchId, UserId = scenario.User.Id, RequestId = Guid.NewGuid(), CreatedAt = now, Timezone = "UTC",
                ReferenceDate = DateOnly.FromDateTime(now.UtcDateTime),
                Rows =
                [
                    new SessionDraftRow
                    {
                        Id = draftId, Position = 0, Title = source.Title, GameId = GameId.Encode(source.Id),
                        SelectionJson = $$"""{"Source":"local","GameId":"{{GameId.Encode(source.Id)}}","Title":"{{source.Title}}"}""",
                    },
                    new SessionDraftRow
                    {
                        Id = otherDraftId, Position = 1, Title = neighbor.Title, GameId = GameId.Encode(neighbor.Id),
                        SelectionJson = $$"""{"Source":"local","GameId":"{{GameId.Encode(neighbor.Id)}}"}""",
                    },
                ],
            });
            await db.SaveChangesAsync();
        }

        try
        {
            using (var merged = await client.PostAsJsonAsync("/api/admin/games/merge", new { sourceId = source.Id, targetId = scenario.Game.Id }))
                Assert.Equal(HttpStatusCode.NoContent, merged.StatusCode);
            await using (var db = fixture.CreateDb())
            {
                Assert.False(await db.Games.AnyAsync(row => row.Id == source.Id));
                var target = await db.Games.AsNoTracking().SingleAsync(row => row.Id == scenario.Game.Id);
                Assert.Equal(source.Title, target.Title);
                Assert.Equal(source.CapsuleUrl, target.CapsuleUrl);
                Assert.Equal(scenario.Game.Id, await db.GameExternalIdentities.Where(row => row.ExternalId == $"merge-{scenario.Suffix}").Select(row => row.GameId).SingleAsync());
                Assert.Equal(scenario.Game.Id, await db.PlaySessions.Where(row => row.Id == session.Id).Select(row => row.GameId).SingleAsync());
                Assert.Single(await db.IgnoredGames.Where(row => row.UserId == scenario.User.Id && row.GameId == scenario.Game.Id).ToListAsync());
                Assert.Equal((short)5, await db.GameRatings.Where(row => row.UserId == scenario.User.Id && row.GameId == scenario.Game.Id).Select(row => row.Rating).SingleAsync());
                Assert.True(await db.GameConnections.AnyAsync(row => row.UserId == scenario.User.Id &&
                    (row.GameId == scenario.Game.Id || row.ConnectedGameId == scenario.Game.Id) &&
                    (row.GameId == neighbor.Id || row.ConnectedGameId == neighbor.Id)));

                var draft = await db.SessionDraftRows.AsNoTracking().SingleAsync(row => row.Id == draftId);
                Assert.Equal(GameId.Encode(scenario.Game.Id), draft.GameId);
                Assert.Contains($"\"GameId\": \"{GameId.Encode(scenario.Game.Id)}\"", draft.SelectionJson, StringComparison.Ordinal);
                Assert.Contains("\"Source\": \"local\"", draft.SelectionJson, StringComparison.Ordinal);
                var other = await db.SessionDraftRows.AsNoTracking().SingleAsync(row => row.Id == otherDraftId);
                Assert.Equal(GameId.Encode(neighbor.Id), other.GameId);
                Assert.Contains(GameId.Encode(neighbor.Id), other.SelectionJson, StringComparison.Ordinal);
            }

            using (var same = await client.PostAsJsonAsync("/api/admin/games/merge", new { sourceId = scenario.Game.Id, targetId = scenario.Game.Id }))
                Assert.Equal(HttpStatusCode.BadRequest, same.StatusCode);
            using var missing = await client.PostAsJsonAsync("/api/admin/games/merge", new { sourceId = long.MaxValue, targetId = scenario.Game.Id });
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
            Assert.Equal("Source game not found.", await AdminApiFixture.ReadErrorAsync(missing));
        }
        finally
        {
            await using var db = fixture.CreateDb();
            await db.SessionDraftBatches.Where(row => row.Id == batchId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GameMergeRollsBackEveryEarlierChangeWhenRelationshipReassignmentFails()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        var source = await scenario.AddGameAsync($"Rollback source {scenario.Suffix}", true);
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, false, "rollback");
        await scenario.AddSessionAsync(source.Id, integration.Id);
        await using (var db = fixture.CreateDb())
        {
            var now = DateTimeOffset.UtcNow;
            db.GameExternalIdentities.Add(new GameExternalIdentity
            {
                Id = scenario.NextEntityId(), GameId = source.Id, Type = GameExternalIdentityType.Psn,
                ExternalId = $"rollback-{scenario.Suffix}", Title = source.Title, CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        var options = new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(fixture.Factory.Services.GetRequiredService<NpgsqlDataSource>(), npgsql =>
            {
                npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                npgsql.MapEnum<IntegrationType>("integration_type");
                npgsql.MapEnum<CaptureMethod>("capture_method");
            })
            .AddInterceptors(new FailSessionReassignmentInterceptor())
            .Options;
        await using (var failing = new PlaydexDbContext(options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new AdminRepository(failing).MergeGames(source.Id, scenario.Game.Id, CancellationToken.None));
        }

        await using var verification = fixture.CreateDb();
        Assert.True(await verification.Games.AnyAsync(row => row.Id == source.Id));
        Assert.Equal($"Target {scenario.Suffix}", await verification.Games.Where(row => row.Id == scenario.Game.Id).Select(row => row.Title).SingleAsync());
        Assert.Equal(source.Id, await verification.GameExternalIdentities
            .Where(row => row.ExternalId == $"rollback-{scenario.Suffix}")
            .Select(row => row.GameId)
            .SingleAsync());
    }

    [Fact]
    public async Task MutationsAreAuditedWithoutLeakingSecrets()
    {
        if (fixture.Factory is null) return;
        await using var scenario = await Scenario.CreateAsync(fixture);
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var integration = await scenario.AddIntegrationAsync(IntegrationType.Steam, scenario.Platform.Id, true, "audit");
        var target = await scenario.AddIntegrationAsync(IntegrationType.Api, null, false, "audit-target");
        var session = await scenario.AddSessionAsync(scenario.Game.Id, integration.Id);
        var source = await scenario.AddGameAsync($"Audit source {scenario.Suffix}");
        const string secret = "super-secret-api-key-for-audit";

        using (var r = await client.PutAsJsonAsync($"/api/admin/platforms/{scenario.Platform.Key}", new { enabled = false })) r.EnsureSuccessStatusCode();
        await Patch(client, $"/api/admin/integrations/{integration.Id}", new { apiKeyChanged = true, apiKey = secret, accountIdChanged = false });
        await Transfer(client, [session.Id], target.Id, expected: 1);
        await Patch(client, $"/api/admin/games/{scenario.Game.Id}", new { title = "Audited target", titleChanged = true });
        using (var r = await client.PostAsJsonAsync("/api/admin/games/merge", new { sourceId = source.Id, targetId = scenario.Game.Id })) r.EnsureSuccessStatusCode();

        var entries = fixture.Logs.Entries.Where(row => row.StartsWith("Admin mutation", StringComparison.Ordinal)).ToList();
        Assert.Contains(entries, row => row.Contains("set-platform-enabled", StringComparison.Ordinal) && row.Contains(scenario.Platform.Key, StringComparison.Ordinal));
        Assert.Contains(entries, row => row.Contains("update-integration", StringComparison.Ordinal) && row.Contains(integration.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Contains(entries, row => row.Contains("transfer-sessions", StringComparison.Ordinal) && row.Contains(target.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Contains(entries, row => row.Contains("update-game", StringComparison.Ordinal) && row.Contains(scenario.Game.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.Contains(entries, row => row.Contains("merge-games", StringComparison.Ordinal) && row.Contains(source.Id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.All(entries, row => Assert.Contains("correlation", row, StringComparison.Ordinal));
        Assert.DoesNotContain(fixture.Logs.Entries, row => row.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerationHistoryPaginatesFiltersShowsDetailsServesImagesAndDeletes()
    {
        if (fixture.Factory is null) return;
        using var client = await fixture.LoginAsync(AdminApiFixture.AdminEmail);
        var marker = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var records = Enumerable.Range(0, 27).Select(i => new AiGeneration
        {
            Id = Guid.NewGuid(), UserId = fixture.AdminId, RequestId = Guid.NewGuid(), CreatedAt = now.AddSeconds(i),
            Provider = "test", Model = "test-model", Timezone = "Europe/Warsaw", ReferenceDate = new DateOnly(2026, 9, 16),
            InputText = marker + new string('x', 200), Image = [1, 2, 3], ImageContentType = "image/png",
            Status = i == 0 ? "failure" : "success", Error = i == 0 ? "Provider failed." : null,
            ResponsesJson = i == 0
                ? """[{"HttpStatus":500,"Body":"{\"error\":\"boom\"}","Truncated":true},{"Body":"malformed"}]"""
                : "[]",
            ResultJson = i == 0
                ? """{"Rows":[{"Title":"Hades","Date":"2026-09-15","DurationMinutes":90,"PlatformKey":"pc"}]}"""
                : null,
        }).ToArray();
        await using var db = fixture.CreateDb();
        db.AiGenerations.AddRange(records);
        await db.SaveChangesAsync();
        try
        {
            var first = await Generations(client, $"query={marker}");
            Assert.Equal(27, first.Total);
            Assert.Equal(25, first.Items.Count);
            Assert.Equal(records[^1].Id, first.Items[0].Id);
            Assert.Equal(100, first.Items[0].InputPreview!.Length);
            Assert.True(first.Items[0].HasImage);
            Assert.Equal(2, (await Generations(client, $"query={marker}&page=1")).Items.Count);
            Assert.Single((await Generations(client, $"query={marker}&status=failure")).Items);
            Assert.Single((await Generations(client, $"query={marker}&status=errors")).Items);
            Assert.Equal(26, (await Generations(client, $"query={marker}&status=success")).Total);
            Assert.Equal(2, (await Generations(client, $"query={marker}&since={Iso(now.AddSeconds(25))}")).Total);

            var detail = await AdminApiFixture.ReadDataAsync<AdminGenerationDetailDto>(
                await client.GetAsync($"/api/admin/ai-generations/{records[0].Id}"));
            Assert.Equal(AdminApiFixture.AdminEmail, detail.Email);
            Assert.Equal(records[0].InputText, detail.InputText);
            Assert.Equal("2026-09-16", detail.ReferenceDate);
            Assert.Equal("Provider failed.", detail.Error);
            Assert.True(detail.HasImage);
            var resultRow = Assert.Single(detail.Rows);
            Assert.Equal(("Hades", "2026-09-15", (int?)90, "pc"), (resultRow.Title, resultRow.Date, resultRow.DurationMinutes, resultRow.PlatformKey));
            Assert.Contains("\n", detail.Result, StringComparison.Ordinal);
            Assert.Equal(2, detail.Responses.Count);
            Assert.Equal((500, true), (detail.Responses[0].HttpStatus, detail.Responses[0].Truncated));
            Assert.Contains("\"error\": \"boom\"", detail.Responses[0].Body, StringComparison.Ordinal);
            Assert.Equal("malformed", detail.Responses[1].Body);

            using (var image = await client.GetAsync($"/api/admin/ai-generations/{records[0].Id}/image"))
            {
                Assert.Equal(HttpStatusCode.OK, image.StatusCode);
                Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
                Assert.True(image.Headers.CacheControl?.NoStore);
                Assert.Equal("nosniff", Assert.Single(image.Headers.GetValues("X-Content-Type-Options")));
                Assert.Equal(new byte[] { 1, 2, 3 }, await image.Content.ReadAsByteArrayAsync());
            }

            using (var deleted = await client.DeleteAsync($"/api/admin/ai-generations/{records[0].Id}"))
                Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            using (var gone = await client.GetAsync($"/api/admin/ai-generations/{records[0].Id}"))
            {
                Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
                Assert.Equal("This generation has been deleted or expired.", await AdminApiFixture.ReadErrorAsync(gone));
            }

            using var noImage = await client.GetAsync($"/api/admin/ai-generations/{records[0].Id}/image");
            Assert.Equal(HttpStatusCode.NotFound, noImage.StatusCode);
        }
        finally
        {
            await db.AiGenerations.Where(row => records.Select(item => item.Id).Contains(row.Id)).ExecuteDeleteAsync();
        }
    }

    private static string Iso(DateTimeOffset value) =>
        Uri.EscapeDataString(value.ToString("O", CultureInfo.InvariantCulture));

    private static async Task<AdminDashboardDto> Dashboard(HttpClient client, string query, string? platform,
        DateTimeOffset dayStart, DateTimeOffset dayEnd) =>
        await AdminApiFixture.ReadDataAsync<AdminDashboardDto>(await client.GetAsync(
            $"/api/admin/dashboard?query={Uri.EscapeDataString(query)}&platform={Uri.EscapeDataString(platform ?? "")}&dayStart={Iso(dayStart)}&dayEnd={Iso(dayEnd)}"));

    private static async Task<AdminSessionPageDto> Sessions(HttpClient client, string query, int page, string? status = null) =>
        await AdminApiFixture.ReadDataAsync<AdminSessionPageDto>(await client.GetAsync(
            $"/api/admin/sessions?query={Uri.EscapeDataString(query)}&page={page}&status={status}"));

    private static async Task<List<AdminGameDto>> Games(HttpClient client, string query) =>
        await AdminApiFixture.ReadDataAsync<List<AdminGameDto>>(await client.GetAsync(
            $"/api/admin/games?query={Uri.EscapeDataString(query)}"));

    private static async Task<AdminGenerationPageDto> Generations(HttpClient client, string queryString) =>
        await AdminApiFixture.ReadDataAsync<AdminGenerationPageDto>(await client.GetAsync($"/api/admin/ai-generations?{queryString}"));

    private static async Task Patch(HttpClient client, string path, object body)
    {
        using var response = await client.PatchAsJsonAsync(path, body);
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"{path}: {await response.Content.ReadAsStringAsync()}");
    }

    private static async Task Transfer(HttpClient client, long[] sessionIds, long targetId, int expected)
    {
        var result = await AdminApiFixture.ReadDataAsync<AdminTransferSessionsResultDto>(await client.PostAsJsonAsync(
            "/api/admin/sessions/transfer", new { sessionIds, targetIntegrationId = targetId }));
        Assert.Equal(expected, result.Transferred);
    }

    private sealed record PlatformSummary(string Key, string Name, int Total, int Active, bool Enabled);

    private sealed class FailSessionReassignmentInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("update play_sessions", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("set game_id", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Injected relationship reassignment failure.");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Scenario : IAsyncDisposable
    {
        private readonly AdminApiFixture fixture;
        private readonly List<long> userIds = [];
        private readonly List<long> platformIds = [];
        private readonly List<long> gameIds = [];
        private long nextEntityId;
        private long nextSessionId = Random.Shared.NextInt64(8_000_000_000_000_000_000, 8_500_000_000_000_000_000);

        private Scenario(AdminApiFixture fixture, string suffix, User user, Platform platform, Game game, long nextEntityId)
        {
            this.fixture = fixture;
            Suffix = suffix;
            User = user;
            Platform = platform;
            Game = game;
            this.nextEntityId = nextEntityId;
            userIds.Add(user.Id);
            platformIds.Add(platform.Id);
            gameIds.Add(game.Id);
        }

        public static readonly DateTimeOffset OldTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public string Suffix { get; }
        public User User { get; }
        public Platform Platform { get; }
        public Game Game { get; }

        public static async Task<Scenario> CreateAsync(AdminApiFixture fixture)
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            await using var db = fixture.CreateDb();
            var entityBase = Random.Shared.NextInt64(7_100_000_000_000, 7_150_000_000_000);
            var user = new User { Id = entityBase, Email = $"admin-api-{suffix}@example.test", CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            var platform = new Platform { Id = entityBase + 1, Key = $"adm-{suffix}", Name = $"Admin {suffix}", CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            var game = new Game { Id = entityBase + 2, Title = $"Target {suffix}", CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.AddRange(user, platform, game);
            await db.SaveChangesAsync();
            return new Scenario(fixture, suffix, user, platform, game, entityBase + 3);
        }

        public async Task<User> AddUserAsync(string email)
        {
            await using var db = fixture.CreateDb();
            var user = new User { Id = NextEntityId(), Email = email, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            userIds.Add(user.Id);
            return user;
        }

        public async Task<Platform> AddPlatformAsync(string key)
        {
            await using var db = fixture.CreateDb();
            var platform = new Platform { Id = NextEntityId(), Key = key, Name = key, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Platforms.Add(platform);
            await db.SaveChangesAsync();
            platformIds.Add(platform.Id);
            return platform;
        }

        public async Task<Integration> AddIntegrationAsync(IntegrationType type, long? platformId, bool active,
            string? accountId, string? apiKey = null, long? userId = null)
        {
            await using var db = fixture.CreateDb();
            var integration = new Integration
            {
                Id = NextEntityId(), UserId = userId ?? User.Id, Type = type, PlatformId = platformId, Active = active,
                AccountId = accountId, ApiKey = apiKey, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp,
            };
            db.Integrations.Add(integration);
            await db.SaveChangesAsync();
            return integration;
        }

        public async Task<Game> AddGameAsync(string title, bool titleLocked = false, string? capsuleUrl = null)
        {
            await using var db = fixture.CreateDb();
            var game = new Game { Id = NextEntityId(), Title = title, TitleLocked = titleLocked, CapsuleUrl = capsuleUrl, CreatedAt = OldTimestamp, UpdatedAt = OldTimestamp };
            db.Games.Add(game);
            await db.SaveChangesAsync();
            gameIds.Add(game.Id);
            return game;
        }

        public async Task AddGamesAsync(int count, string titlePrefix)
        {
            await using var db = fixture.CreateDb();
            var games = Enumerable.Range(0, count).Select(index => new Game
            {
                Id = NextEntityId(), Title = $"{titlePrefix} {index:D2}",
                CreatedAt = OldTimestamp.AddMinutes(index), UpdatedAt = DateTimeOffset.UtcNow.AddSeconds(-index - 1),
            }).ToList();
            db.Games.AddRange(games);
            await db.SaveChangesAsync();
            gameIds.AddRange(games.Select(row => row.Id));
        }

        public async Task UpdateGameAsync()
        {
            await using var db = fixture.CreateDb();
            var updated = await db.Games.Where(row => row.Id == Game.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.Title, Game.Title)
                .SetProperty(row => row.CapsuleUrl, Game.CapsuleUrl)
                .SetProperty(row => row.UpdatedAt, DateTimeOffset.UtcNow));
            Assert.Equal(1, updated);
        }

        public async Task AddSessionsAsync(int count, DateTimeOffset started, long integrationId)
        {
            for (var index = 0; index < count; index++)
                await AddSessionAsync(Game.Id, integrationId, started.AddMinutes(index),
                    index == count - 1 ? null : started.AddMinutes(index + 1));
        }

        public async Task<PlaySession> AddSessionAsync(long gameId, long integrationId, DateTimeOffset? started = null,
            DateTimeOffset? ended = null)
        {
            await using var db = fixture.CreateDb();
            var integration = await db.Integrations.AsNoTracking().SingleAsync(row => row.Id == integrationId);
            var session = new PlaySession
            {
                Id = nextSessionId++,
                GameId = gameId,
                UserId = integration.UserId,
                PlatformId = integration.PlatformId ?? Platform.Id,
                IntegrationId = integration.Id,
                CaptureMethod = integration.Type == IntegrationType.Api ? CaptureMethod.Api : CaptureMethod.Polling,
                StartedAt = started ?? DateTimeOffset.UtcNow.AddHours(-1),
                EndedAt = ended,
                CreatedAt = OldTimestamp,
                UpdatedAt = OldTimestamp,
            };
            db.PlaySessions.Add(session);
            await db.SaveChangesAsync();
            return session;
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = fixture.CreateDb();
            await db.GameConnections.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.IgnoredGames.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.GameRatings.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.PlaySessions.Where(row => userIds.Contains(row.UserId) || gameIds.Contains(row.GameId)).ExecuteDeleteAsync();
            await db.PollResults.Where(row => db.Integrations.Any(i => i.Id == row.IntegrationId && userIds.Contains(i.UserId))).ExecuteDeleteAsync();
            await db.GameExternalIdentities.Where(row => gameIds.Contains(row.GameId)).ExecuteDeleteAsync();
            await db.Integrations.Where(row => userIds.Contains(row.UserId)).ExecuteDeleteAsync();
            await db.Games.Where(row => gameIds.Contains(row.Id)).ExecuteDeleteAsync();
            await db.Platforms.Where(row => platformIds.Contains(row.Id)).ExecuteDeleteAsync();
            await db.Users.Where(row => userIds.Contains(row.Id)).ExecuteDeleteAsync();
        }

        public long NextEntityId() => nextEntityId++;
    }
}
