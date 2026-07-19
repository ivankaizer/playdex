using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Modules.Accounts;
using Playdex.Api.Shared.Common;
using Playdex.Api.Shared.Persistence;
using Xunit;

namespace Playdex.Api.Tests;

[Collection(HttpIntegrationCollection.Name)]
public sealed class AccountsParityIntegrationTests
{
    [Fact]
    public async Task SessionsMarkShortRowsStatsExcludeThemAndConnectedGamesMerge()
    {
        var databaseUrl = RequireDatabaseUrl();
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("N");
        var userId = await InsertUser(dataSource, $"accounts-parity-{suffix}@example.com");
        var platformId = await Scalar<long>(dataSource, "select id from platforms where key = 'steam'");
        var credentialId = await InsertCredential(dataSource, userId, platformId, suffix);
        var firstGameId = await InsertGame(dataSource, platformId, $"First {suffix}", $"steam:first-{suffix}");
        var secondGameId = await InsertGame(dataSource, platformId, $"Second {suffix}", $"steam:second-{suffix}");

        try
        {
            await Execute(dataSource, """
                insert into user_settings (user_id, ignore_short_sessions_minutes)
                values (@userId, 10)
                """, new NpgsqlParameter("userId", userId));
            await Execute(dataSource, """
                insert into game_connections (user_id, game_id, connected_game_id)
                values (@userId, @firstGameId, @secondGameId)
                """,
                new NpgsqlParameter("userId", userId),
                new NpgsqlParameter("firstGameId", Math.Min(firstGameId, secondGameId)),
                new NpgsqlParameter("secondGameId", Math.Max(firstGameId, secondGameId)));
            await Execute(dataSource, """
                insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
                values
                    (@credentialId, @firstGameId, '2026-07-15T10:00:00Z', '2026-07-15T10:05:00Z'),
                    (@credentialId, @firstGameId, '2026-07-15T11:00:00Z', '2026-07-15T11:20:00Z'),
                    (@credentialId, @secondGameId, '2026-07-15T12:00:00Z', '2026-07-15T12:30:00Z')
                """,
                new NpgsqlParameter("credentialId", credentialId),
                new NpgsqlParameter("firstGameId", firstGameId),
                new NpgsqlParameter("secondGameId", secondGameId));

            await using var db = Context(dataSource);
            var service = new AccountsService(new AccountsRepository(db, new StubGames()));
            var sessions = await service.ListSessions(
                userId, null, null, GameId.Encode(firstGameId),
                "2026-07-15", "2026-07-15", "date", "asc", 10, 0, 0, CancellationToken.None);
            var stats = await service.ComputeStatsForRange(
                userId, null, "2026-07-15", "2026-07-15", 0, CancellationToken.None);

            Assert.Equal(3, sessions.Total);
            Assert.True(sessions.Sessions[0].Ignored);
            Assert.False(sessions.Sessions[1].Ignored);
            Assert.False(sessions.Sessions[2].Ignored);
            Assert.Equal(2, stats.SessionCount);
            Assert.Equal(3000, stats.TotalSeconds);
            var top = Assert.Single(stats.TopGames);
            Assert.Equal(GameId.Encode(secondGameId), top.GameId);
            Assert.Equal($"Second {suffix}", top.Title);
            Assert.Equal(3000, top.TotalSeconds);
        }
        finally
        {
            await Execute(dataSource, "delete from users where id = @id", new NpgsqlParameter("id", userId));
            await Execute(dataSource, "delete from games where id = any(@ids)",
                new NpgsqlParameter("ids", new[] { firstGameId, secondGameId }));
        }
    }

    [Fact]
    public async Task ManualMappingProjectsMappedGamePlatformSourceAndCapsule()
    {
        var databaseUrl = RequireDatabaseUrl();
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("N");
        var userId = await InsertUser(dataSource, $"accounts-mapping-{suffix}@example.com");
        var manualPlatformId = await Scalar<long>(dataSource, "select id from platforms where key = 'manual'");
        var pcPlatformId = await Scalar<long>(dataSource, "select id from platforms where key = 'pc'");
        var credentialId = await InsertCredential(dataSource, userId, manualPlatformId, suffix);
        var rawTitle = $"Mapped Raw {suffix}";
        var rawGameId = await InsertGame(dataSource, manualPlatformId, rawTitle, $"manual:mapped-raw-{suffix}");
        var mappedGameId = await Scalar<long>(dataSource, """
            insert into games (platform_id, title, external_id, canonical_key, capsule_url)
            values (@platformId, @title, '123', @canonicalKey, @capsuleUrl)
            returning id
            """,
            new NpgsqlParameter("platformId", pcPlatformId),
            new NpgsqlParameter("title", $"Mapped Target {suffix}"),
            new NpgsqlParameter("canonicalKey", $"igdb:{suffix}"),
            new NpgsqlParameter("capsuleUrl", $"https://example.test/{suffix}.jpg"));

        try
        {
            await Execute(dataSource, """
                insert into manual_game_inputs (user_id, raw_title, canonical_title, mapped_game_id)
                values (@userId, @rawTitle, @canonicalTitle, @mappedGameId)
                """,
                new NpgsqlParameter("userId", userId),
                new NpgsqlParameter("rawTitle", rawTitle),
                new NpgsqlParameter("canonicalTitle", Normalize(rawTitle)),
                new NpgsqlParameter("mappedGameId", mappedGameId));
            await Execute(dataSource, """
                insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
                values (@credentialId, @gameId, '2026-07-16T10:00:00Z', '2026-07-16T11:00:00Z')
                """,
                new NpgsqlParameter("credentialId", credentialId),
                new NpgsqlParameter("gameId", rawGameId));

            await using var db = Context(dataSource);
            var service = new AccountsService(new AccountsRepository(db, new StubGames()));
            var result = await service.ListSessions(
                userId, null, null, null, "2026-07-16", "2026-07-16",
                "date", "desc", 10, 0, 0, CancellationToken.None);

            var session = Assert.Single(result.Sessions);
            Assert.Equal(GameId.Encode(mappedGameId), session.GameId);
            Assert.Equal($"Mapped Target {suffix}", session.Game);
            Assert.Equal("manual", session.Platform);
            Assert.Equal("pc", session.MappedPlatform);
            Assert.Equal("igdb", session.MappedSource);
            Assert.Equal($"https://example.test/{suffix}.jpg", session.CapsuleUrl);
        }
        finally
        {
            await Execute(dataSource, "delete from users where id = @id", new NpgsqlParameter("id", userId));
            await Execute(dataSource, "delete from games where id = any(@ids)",
                new NpgsqlParameter("ids", new[] { rawGameId, mappedGameId }));
        }
    }

    private static PlaydexDbContext Context(NpgsqlDataSource dataSource) => new(
        new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(dataSource).Options);

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static Task<long> InsertUser(NpgsqlDataSource source, string email) =>
        Scalar<long>(source, "insert into users (email) values (@email) returning id", new NpgsqlParameter("email", email));

    private static Task<long> InsertCredential(NpgsqlDataSource source, long userId, long platformId, string suffix) =>
        Scalar<long>(source, """
            insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
            values (@userId, @platformId, @key, @account, true)
            returning id
            """,
            new NpgsqlParameter("userId", userId),
            new NpgsqlParameter("platformId", platformId),
            new NpgsqlParameter("key", $"key-{suffix}"),
            new NpgsqlParameter("account", $"account-{suffix}"));

    private static Task<long> InsertGame(
        NpgsqlDataSource source,
        long platformId,
        string title,
        string canonicalKey) =>
        Scalar<long>(source, """
            insert into games (platform_id, title, canonical_key)
            values (@platformId, @title, @canonicalKey)
            returning id
            """,
            new NpgsqlParameter("platformId", platformId),
            new NpgsqlParameter("title", title),
            new NpgsqlParameter("canonicalKey", canonicalKey));

    private static string RequireDatabaseUrl() =>
        Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")
        ?? throw new InvalidOperationException("PLAYDEX_INTEGRATION_DATABASE_URL is required.");

    private static async Task<T> Scalar<T>(
        NpgsqlDataSource source,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Execute(
        NpgsqlDataSource source,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = await source.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
