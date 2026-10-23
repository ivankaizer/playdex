using Playdex.Persistence;
using Api.Modules.Providers;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class ProvidersRepositoryTests(PostgresFixture postgres)
{
    [Fact]
    public void SessionPlatformOptionsContainMainAndRecursiveChildrenOnly()
    {
        var main = new Platform { Id = 10, Key = "psn", Name = "PSN" };
        var child = new Platform { Id = 11, Key = "ps5", Name = "PS5", ParentId = main.Id };
        var grandchild = new Platform { Id = 12, Key = "ps5-pro", Name = "PS5 Pro", ParentId = child.Id };
        var unrelated = new Platform { Id = 20, Key = "pc", Name = "PC" };
        var result = ProvidersRepository.BuildSessionPlatformOptions(
            main.Id,
            [unrelated, grandchild, main, child]);
        var keys = result.Select(item => item.Key).ToArray();

        Assert.Equal(["psn", "ps5", "ps5-pro"], keys);
    }

    [Fact]
    public async Task SessionPlatformUpdateRewritesAllExistingSessionsOrNone()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("n");
        var psn = await db.Platforms.SingleAsync(platform => platform.Key == "psn");
        var ps4 = await db.Platforms.SingleAsync(platform => platform.Key == "ps4");
        var ps5 = await db.Platforms.SingleAsync(platform => platform.Key == "ps5");
        var user = new User
        {
            Email = $"provider-platform-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var game = new Game { Title = $"Platform test {suffix}", CreatedAt = now, UpdatedAt = now };
        db.AddRange(user, game);
        await db.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Psn,
            PlatformId = psn.Id,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var apiIntegration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Api,
            PlatformId = null,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Integrations.AddRange(integration, apiIntegration);
        await db.SaveChangesAsync();
        var session = new PlaySession
        {
            UserId = user.Id,
            GameId = game.Id,
            PlatformId = psn.Id,
            CaptureMethod = CaptureMethod.Polling,
            IntegrationId = integration.Id,
            StartedAt = now.AddHours(-1),
            EndedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.PlaySessions.Add(session);
        await db.SaveChangesAsync();

        try
        {
            var repository = new ProvidersRepository(db, TestSessions.Lifecycle(db));
            var futureOnly = await repository.UpdateSessionPlatform(
                user.Id, integration.Id, "ps5", rewriteExistingSessions: false, default);

            Assert.False(futureOnly.InvalidPlatform);
            Assert.Equal("ps5", futureOnly.Provider!.SessionPlatform!.Key);
            Assert.Equal(psn.Id, await db.PlaySessions.AsNoTracking()
                .Where(item => item.Id == session.Id)
                .Select(item => item.PlatformId)
                .SingleAsync());

            var rewriteAll = await repository.UpdateSessionPlatform(
                user.Id, integration.Id, "ps4", rewriteExistingSessions: true, default);

            Assert.False(rewriteAll.InvalidPlatform);
            Assert.Equal(ps4.Id, await db.PlaySessions.AsNoTracking()
                .Where(item => item.Id == session.Id)
                .Select(item => item.PlatformId)
                .SingleAsync());

            var selectMain = await repository.UpdateSessionPlatform(
                user.Id, integration.Id, "psn", rewriteExistingSessions: true, default);

            Assert.Null((await db.Integrations.AsNoTracking()
                .SingleAsync(item => item.Id == integration.Id)).SessionPlatformId);
            Assert.Equal(psn.Id, await db.PlaySessions.AsNoTracking()
                .Where(item => item.Id == session.Id)
                .Select(item => item.PlatformId)
                .SingleAsync());

            var unrelated = await repository.UpdateSessionPlatform(
                user.Id, integration.Id, "pc", rewriteExistingSessions: true, default);
            Assert.True(unrelated.InvalidPlatform);
            Assert.Null(unrelated.Provider);

            var unsupported = await repository.UpdateSessionPlatform(
                user.Id, apiIntegration.Id, "psn", rewriteExistingSessions: false, default);
            Assert.True(unsupported.UnsupportedIntegration);
            Assert.False(unsupported.InvalidPlatform);

            var missing = await repository.UpdateSessionPlatform(
                user.Id, long.MaxValue, "psn", rewriteExistingSessions: false, default);
            Assert.False(missing.UnsupportedIntegration);
            Assert.False(missing.InvalidPlatform);
            Assert.Null(missing.Provider);
        }
        finally
        {
            db.PlaySessions.RemoveRange(db.PlaySessions.Where(item => item.IntegrationId == integration.Id));
            db.Integrations.RemoveRange(integration, apiIntegration);
            db.Games.Remove(game);
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SessionPlatformUpdateWaitsForAnInFlightPollOnTheSameIntegration()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var setupDb = CreateDb();
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("n");
        var psn = await setupDb.Platforms.SingleAsync(platform => platform.Key == "psn");
        var user = new User
        {
            Email = $"provider-lock-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        setupDb.Users.Add(user);
        await setupDb.SaveChangesAsync();
        var integration = new Integration
        {
            UserId = user.Id,
            Type = IntegrationType.Psn,
            PlatformId = psn.Id,
            Active = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
        setupDb.Integrations.Add(integration);
        await setupDb.SaveChangesAsync();

        try
        {
            await using var pollingDb = CreateDb();
            await using var pollingTransaction = await pollingDb.Database.BeginTransactionAsync();
            await pollingDb.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock({integration.Id})");

            await using var updateDb = CreateDb();
            var updateTask = new ProvidersRepository(updateDb, TestSessions.Lifecycle(updateDb)).UpdateSessionPlatform(
                user.Id,
                integration.Id,
                "ps5",
                rewriteExistingSessions: true,
                default);
            try
            {
                var firstCompleted = await Task.WhenAny(updateTask, Task.Delay(250));
                Assert.NotSame(updateTask, firstCompleted);
            }
            finally
            {
                await pollingTransaction.CommitAsync();
            }

            var result = await updateTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal("ps5", result.Provider!.SessionPlatform!.Key);
        }
        finally
        {
            setupDb.Integrations.Remove(integration);
            setupDb.Users.Remove(user);
            await setupDb.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CreateProviderAllowsThreeOfATypeAndRejectsTheFourth()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var db = CreateDb();
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var user = new User
        {
            Email = $"provider-creation-{suffix}@example.test",
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        try
        {
            var repository = new ProvidersRepository(db, TestSessions.Lifecycle(db));
            var steam1 = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-1", "steam-id-1", default);
            var steam2 = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-2", "steam-id-2", default);
            var steam3 = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-3", "steam-id-3", default);
            var psn = await repository.CreateProvider(
                user.Id, "psn", IntegrationType.Psn, "npsso-token", null, default);
            var xbox = await repository.CreateProvider(
                user.Id, "xbox", IntegrationType.Xbox, "openxbl-key", "2533274817903789", default);
            var fourthSteam = await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key-4", "steam-id-4", default);

            Assert.Equal("active", steam1.Provider?.Status);
            Assert.Equal("active", steam2.Provider?.Status);
            Assert.Equal("active", steam3.Provider?.Status);
            Assert.Equal("active", psn.Provider?.Status);
            Assert.Equal("active", xbox.Provider?.Status);
            Assert.False(steam1.LimitReached);
            Assert.False(steam2.LimitReached);
            Assert.False(steam3.LimitReached);
            Assert.False(psn.LimitReached);
            Assert.False(xbox.LimitReached);
            Assert.True(fourthSteam.LimitReached);
            Assert.Null(fourthSteam.Provider);

            var persisted = await db.Integrations
                .AsNoTracking()
                .Where(integration => integration.UserId == user.Id)
                .OrderBy(integration => integration.Type)
                .ToListAsync();
            Assert.Equal(5, persisted.Count);
            Assert.All(persisted, integration => Assert.True(integration.Active));
            Assert.Equal(3, persisted.Count(item => item.Type == IntegrationType.Steam));
            Assert.Equal(
                ["steam-key-1", "steam-key-2", "steam-key-3"],
                persisted.Where(item => item.Type == IntegrationType.Steam)
                    .OrderBy(item => item.AccountId)
                    .Select(item => item.ApiKey));
            Assert.Equal("npsso-token", persisted.Single(item => item.Type == IntegrationType.Psn).ApiKey);
            Assert.Equal("openxbl-key", persisted.Single(item => item.Type == IntegrationType.Xbox).ApiKey);
        }
        finally
        {
            db.Integrations.RemoveRange(db.Integrations.Where(item => item.UserId == user.Id));
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ParallelCreationsStayWithinTheLimit()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser("provider-limit");
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(1, 6).Select(async index =>
            {
                await using var db = CreateDb();
                return await new ProvidersRepository(db, TestSessions.Lifecycle(db)).CreateProvider(
                    userId, "steam", IntegrationType.Steam, $"key-{index}", $"steam-id-{index}", default);
            }));

            Assert.Equal(3, results.Count(result => result.Provider is not null));
            Assert.Equal(3, results.Count(result => result.LimitReached));
            await using var verify = CreateDb();
            Assert.Equal(3, await verify.Integrations.CountAsync(item => item.UserId == userId));
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    [Fact]
    public async Task AnAccountCanBeConnectedOncePerType()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser("provider-duplicate");
        try
        {
            await using var db = CreateDb();
            var repository = new ProvidersRepository(db, TestSessions.Lifecycle(db));
            var first = await repository.CreateProvider(
                userId, "steam", IntegrationType.Steam, "key-1", "76561190000000003", default);
            var duplicate = await repository.CreateProvider(
                userId, "steam", IntegrationType.Steam, "key-2", "76561190000000003", default);
            var second = await repository.CreateProvider(
                userId, "steam", IntegrationType.Steam, "key-3", "76561190000000004", default);

            Assert.NotNull(first.Provider);
            Assert.True(duplicate.DuplicateAccount);
            Assert.Null(duplicate.Provider);
            await Assert.ThrowsAsync<DuplicateIntegrationAccountException>(() => repository.UpdateCredential(
                userId, second.Provider!.Id, false, null, true, "76561190000000003", ProvidersService.ValidateCredential, default));

            await using var serviceDb = CreateDb();
            var service = new ProvidersService(new ProvidersRepository(serviceDb, TestSessions.Lifecycle(serviceDb)));
            var conflict = await Assert.ThrowsAsync<Api.Infrastructure.Http.AppProblemException>(() =>
                service.CreateProvider(userId, new CreateProviderRequest("steam", "key-4", "76561190000000004"), default));
            Assert.Equal(409, conflict.StatusCode);
            Assert.Equal("This Steam account is already connected.", conflict.Message);
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    [Fact]
    public async Task CredentialUpdatesMustLeaveAPollableCredential()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        // No surrounding transaction: creating and updating providers takes its own.
        var userId = await CreateUser("provider-update");
        try
        {
            await using var db = CreateDb();
            var user = await db.Users.SingleAsync(row => row.Id == userId);
            var repository = new ProvidersRepository(db, TestSessions.Lifecycle(db));
            var service = new ProvidersService(repository);
            var steam = (await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key", "76561198000000000", default)).Provider!;
            var xbox = (await repository.CreateProvider(
                user.Id, "xbox", IntegrationType.Xbox, "openxbl-key", null, default)).Provider!;

            async Task<string> Refused(long providerId, ProviderCredentialChanges changes) =>
                (await Assert.ThrowsAsync<Api.Infrastructure.Http.AppProblemException>(() =>
                    service.UpdateCredential(user.Id, providerId, changes, default))).Message;

            Assert.Equal("Steam Web API key is required.", await Refused(steam.Id, new(true, null, false, null)));
            Assert.Equal("Steam ID is required.", await Refused(steam.Id, new(false, null, true, null)));
            Assert.Equal("Xbox XUID must contain digits only.", await Refused(xbox.Id, new(false, null, true, "gamertag")));

            db.ChangeTracker.Clear();
            var stored = await db.Integrations.AsNoTracking().SingleAsync(item => item.Id == steam.Id);
            Assert.Equal(("steam-key", "76561198000000000"), (stored.ApiKey, stored.AccountId));

            var updated = await service.UpdateCredential(user.Id, xbox.Id, new(false, null, true, "2533274817903789"), default);
            Assert.Equal("2533274817903789", updated.Provider.AccountId);
        }
        finally
        {
            await DeleteUser(userId);
        }
    }

    [Fact]
    public async Task ConnectOpenXblUpdatesTheChosenIntegrationOrTheOldest()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        var userId = await CreateUser("provider-openxbl");
        var otherUserId = await CreateUser("provider-openxbl-other");
        try
        {
            await using var db = CreateDb();
            var user = await db.Users.SingleAsync(row => row.Id == userId);
            var otherUser = await db.Users.SingleAsync(row => row.Id == otherUserId);
            var repository = new ProvidersRepository(db, TestSessions.Lifecycle(db));
            var service = new ProvidersService(repository);

            // Without an Xbox integration, connecting creates one.
            var first = (await service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key-1", null), default)).Provider;
            var second = (await repository.CreateProvider(
                user.Id, "xbox", IntegrationType.Xbox, "key-2", null, default)).Provider!;
            var third = (await repository.CreateProvider(
                user.Id, "xbox", IntegrationType.Xbox, "key-3", null, default)).Provider!;
            var steam = (await repository.CreateProvider(
                user.Id, "steam", IntegrationType.Steam, "steam-key", "76561198000000000", default)).Provider!;
            var foreign = (await repository.CreateProvider(
                otherUser.Id, "xbox", IntegrationType.Xbox, "foreign-key", null, default)).Provider!;

            async Task<string?> StoredKey(long id) =>
                (await db.Integrations.AsNoTracking().SingleAsync(item => item.Id == id)).ApiKey;

            // Without providerId the oldest is updated, whichever order the rows come back in.
            var oldest = await service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key-1b", "2533274817903789"), default);
            Assert.Equal(first.Id, oldest.Provider.Id);
            Assert.Equal("2533274817903789", oldest.Provider.AccountId);
            Assert.Equal("key-2", await StoredKey(second.Id));

            var chosen = await service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key-3b", null, third.Id), default);
            Assert.Equal(third.Id, chosen.Provider.Id);
            Assert.Equal("key-3b", await StoredKey(third.Id));
            Assert.Equal("key-1b", await StoredKey(first.Id));

            // An administrator lock applies to its own integration only.
            await db.Integrations.Where(item => item.Id == second.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(item => item.ApiKeyLocked, true));
            db.ChangeTracker.Clear();
            var locked = await Assert.ThrowsAsync<Api.Infrastructure.Http.AppProblemException>(() =>
                service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key-2b", null, second.Id), default));
            Assert.Equal((400, "OpenXBL API key is managed by an administrator."), (locked.StatusCode, locked.Message));
            Assert.Equal("key-2", await StoredKey(second.Id));
            await service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key-1c", null), default);
            Assert.Equal("key-1c", await StoredKey(first.Id));

            foreach (var notXbox in new[] { steam.Id, foreign.Id, long.MaxValue })
            {
                var missing = await Assert.ThrowsAsync<Api.Infrastructure.Http.AppProblemException>(() =>
                    service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key", null, notXbox), default));
                Assert.Equal((404, "Provider not found."), (missing.StatusCode, missing.Message));
            }
            Assert.Equal("foreign-key", await StoredKey(foreign.Id));

            var invalid = await Assert.ThrowsAsync<Api.Infrastructure.Http.AppProblemException>(() =>
                service.ConnectOpenXbl(user.Id, new OpenXblConnectRequest("key", null, 0), default));
            Assert.Equal((400, "providerId must be a positive integer."), (invalid.StatusCode, invalid.Message));
        }
        finally
        {
            await DeleteUser(userId);
            await DeleteUser(otherUserId);
        }
    }

    private async Task<long> CreateUser(string prefix)
    {
        await using var db = CreateDb();
        var user = new User { Email = $"{prefix}-{Guid.NewGuid():n}@example.test" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task DeleteUser(long userId)
    {
        await using var db = CreateDb();
        await db.Integrations.Where(item => item.UserId == userId).ExecuteDeleteAsync();
        await db.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
    }

    private PlaydexDbContext CreateDb() => new(
        new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(
                postgres.DataSource!,
                npgsql =>
                {
                    npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
                    npgsql.MapEnum<IntegrationType>("integration_type");
                    npgsql.MapEnum<CaptureMethod>("capture_method");
                })
            .Options);
}
