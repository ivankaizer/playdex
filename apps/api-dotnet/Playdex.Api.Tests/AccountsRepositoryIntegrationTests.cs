using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Modules.Accounts;
using Playdex.Api.Shared.Persistence;
using Xunit;

namespace Playdex.Api.Tests;

[Collection(HttpIntegrationCollection.Name)]
public sealed class AccountsRepositoryIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task DashboardQueries_IncludeTheWholeUtcDayAndProjectManualSessionMetadata()
    {
        var databaseUrl = RequireDatabaseUrl();
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("n");
        long? userId = null;
        long? gameId = null;

        try
        {
            var manualPlatformId = await Scalar<long>(
                dataSource, "select id from platforms where key = 'manual'");
            var gamePlatformId = await Scalar<long>(
                dataSource, "select id from platforms where key = 'unknown'");
            userId = await Scalar<long>(dataSource,
                "insert into users (email) values (@email) returning id",
                new NpgsqlParameter("email", $"dotnet-dashboard-{suffix}@example.com"));
            var credentialId = await Scalar<long>(dataSource, """
                insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
                values (@userId, @platformId, @apiKey, @accountId, true)
                returning id
                """,
                new NpgsqlParameter("userId", userId.Value),
                new NpgsqlParameter("platformId", manualPlatformId),
                new NpgsqlParameter("apiKey", $"manual-{suffix}"),
                new NpgsqlParameter("accountId", $"manual-{suffix}"));
            gameId = await Scalar<long>(dataSource, """
                insert into games (platform_id, title, canonical_key, capsule_url)
                values (@platformId, @title, @canonicalKey, @capsuleUrl)
                returning id
                """,
                new NpgsqlParameter("platformId", gamePlatformId),
                new NpgsqlParameter("title", $"Dashboard integration {suffix}"),
                new NpgsqlParameter("canonicalKey", $"dotnet-dashboard:{suffix}"),
                new NpgsqlParameter("capsuleUrl", $"https://example.test/{suffix}.jpg"));

            await Execute(dataSource, """
                insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
                values
                    (@credentialId, @gameId, '2026-07-12T12:00:00Z', null),
                    (@credentialId, @gameId, '2026-07-13T23:59:59Z', '2026-07-14T00:00:01Z'),
                    (@credentialId, @gameId, '2026-07-14T00:00:00Z', '2026-07-14T00:01:00Z')
                """,
                new NpgsqlParameter("credentialId", credentialId),
                new NpgsqlParameter("gameId", gameId.Value));

            var dbOptions = new DbContextOptionsBuilder<PlaydexDbContext>()
                .UseNpgsql(dataSource)
                .Options;
            await using var db = new PlaydexDbContext(dbOptions);
            var service = new AccountsService(new AccountsRepository(db, new StubGames()));

            var sessions = await service.ListSessions(
                userId.Value, null, null, null,
                "2026-07-13", "2026-07-13", "duration", "asc",
                10, 0, 0, CancellationToken.None);
            var stats = await service.ComputeStatsForRange(
                userId.Value, null, "2026-07-13", "2026-07-13", 0, CancellationToken.None);
            var current = await service.LoadCurrentSession(userId.Value, null, CancellationToken.None);

            var included = Assert.Single(sessions.Sessions);
            Assert.Equal(1, sessions.Total);
            Assert.Equal(new DateTimeOffset(2026, 7, 13, 23, 59, 59, TimeSpan.Zero), included.StartedAt);
            Assert.Equal(1, stats.SessionCount);
            Assert.Equal("unknown", Assert.Single(stats.Platforms).Platform);
            Assert.Equal("unknown", included.MappedPlatform);
            Assert.Equal("local", included.MappedSource);
            Assert.NotNull(current);
            Assert.Equal("manual", current.Platform);
            Assert.Equal("unknown", current.MappedPlatform);
            Assert.Equal("local", current.MappedSource);
            Assert.Equal($"https://example.test/{suffix}.jpg", current.CapsuleUrl);
        }
        finally
        {
            if (userId.HasValue)
            {
                await Execute(dataSource, "delete from users where id = @id",
                    new NpgsqlParameter("id", userId.Value));
            }

            if (gameId.HasValue)
            {
                await Execute(dataSource, "delete from games where id = @id",
                    new NpgsqlParameter("id", gameId.Value));
            }
        }
    }

    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task DeleteSession_ReturnsTheIdOnlyForAnOwnedExistingSession()
    {
        var databaseUrl = RequireDatabaseUrl();
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("n");
        long? ownerId = null;
        long? otherUserId = null;
        long? gameId = null;

        try
        {
            var manualPlatformId = await Scalar<long>(
                dataSource, "select id from platforms where key = 'manual'");
            ownerId = await Scalar<long>(dataSource,
                "insert into users (email) values (@email) returning id",
                new NpgsqlParameter("email", $"dotnet-delete-owner-{suffix}@example.com"));
            otherUserId = await Scalar<long>(dataSource,
                "insert into users (email) values (@email) returning id",
                new NpgsqlParameter("email", $"dotnet-delete-other-{suffix}@example.com"));
            var credentialId = await Scalar<long>(dataSource, """
                insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
                values (@userId, @platformId, @apiKey, @accountId, true)
                returning id
                """,
                new NpgsqlParameter("userId", ownerId.Value),
                new NpgsqlParameter("platformId", manualPlatformId),
                new NpgsqlParameter("apiKey", $"manual-delete-{suffix}"),
                new NpgsqlParameter("accountId", $"manual-delete-{suffix}"));
            gameId = await Scalar<long>(dataSource, """
                insert into games (platform_id, title, canonical_key)
                values (@platformId, @title, @canonicalKey)
                returning id
                """,
                new NpgsqlParameter("platformId", manualPlatformId),
                new NpgsqlParameter("title", $"Delete integration {suffix}"),
                new NpgsqlParameter("canonicalKey", $"dotnet-delete:{suffix}"));
            var ownedSessionId = await Scalar<long>(dataSource, """
                insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
                values (@credentialId, @gameId, '2026-07-13T10:00:00Z', '2026-07-13T11:00:00Z')
                returning id
                """,
                new NpgsqlParameter("credentialId", credentialId),
                new NpgsqlParameter("gameId", gameId.Value));
            var unownedSessionId = await Scalar<long>(dataSource, """
                insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
                values (@credentialId, @gameId, '2026-07-13T12:00:00Z', '2026-07-13T13:00:00Z')
                returning id
                """,
                new NpgsqlParameter("credentialId", credentialId),
                new NpgsqlParameter("gameId", gameId.Value));

            var dbOptions = new DbContextOptionsBuilder<PlaydexDbContext>()
                .UseNpgsql(dataSource)
                .Options;
            await using var db = new PlaydexDbContext(dbOptions);
            var repository = new AccountsRepository(db, new StubGames());

            Assert.Equal(ownedSessionId,
                await repository.DeleteSession(ownedSessionId, ownerId.Value, CancellationToken.None));
            Assert.Equal(0,
                await repository.DeleteSession(long.MaxValue, ownerId.Value, CancellationToken.None));
            Assert.Equal(0,
                await repository.DeleteSession(unownedSessionId, otherUserId.Value, CancellationToken.None));
            Assert.Equal(1, await Scalar<long>(dataSource,
                "select count(*) from play_sessions where id = @id",
                new NpgsqlParameter("id", unownedSessionId)));
        }
        finally
        {
            if (ownerId.HasValue)
            {
                await Execute(dataSource, "delete from users where id = @id",
                    new NpgsqlParameter("id", ownerId.Value));
            }

            if (otherUserId.HasValue)
            {
                await Execute(dataSource, "delete from users where id = @id",
                    new NpgsqlParameter("id", otherUserId.Value));
            }

            if (gameId.HasValue)
            {
                await Execute(dataSource, "delete from games where id = @id",
                    new NpgsqlParameter("id", gameId.Value));
            }
        }
    }

    private static string RequireDatabaseUrl()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL");
        return !string.IsNullOrWhiteSpace(databaseUrl)
            ? databaseUrl
            : throw new InvalidOperationException(
                "PLAYDEX_INTEGRATION_DATABASE_URL is required for PostgreSQL integration tests.");
    }

    private static async Task<T> Scalar<T>(
        NpgsqlDataSource dataSource, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Execute(
        NpgsqlDataSource dataSource, string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
