using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Modules.Admin;
using Playdex.Api.Shared.Persistence;
using Playdex.Api.Modules.Games;
using Playdex.Api.Modules.Providers;
using Xunit;

namespace Playdex.Api.Tests;

[Collection(HttpIntegrationCollection.Name)]
public sealed class RepositoryProjectionIntegrationTests
{
    [Fact]
    [Trait("Category", "PostgreSQLIntegration")]
    public async Task PopulatedRepositoryProjections_ReturnGamesProvidersSessionsAndCredentials()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")
            ?? throw new InvalidOperationException(
                "PLAYDEX_INTEGRATION_DATABASE_URL is required for PostgreSQL integration tests.");
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("n");
        long? userId = null;
        long? firstGameId = null;
        long? secondGameId = null;

        try
        {
            var platformId = await Scalar<long>(
                dataSource, "select id from platforms where key = 'steam'");
            userId = await Scalar<long>(dataSource,
                "insert into users (email) values (@email) returning id",
                new NpgsqlParameter("email", $"projection-{suffix}@example.com"));
            var credentialId = await Scalar<long>(dataSource, """
                insert into platform_credentials
                    (user_id, platform_id, api_key, account_id, active)
                values (@userId, @platformId, @apiKey, @accountId, true)
                returning id
                """,
                new NpgsqlParameter("userId", userId.Value),
                new NpgsqlParameter("platformId", platformId),
                new NpgsqlParameter("apiKey", $"key-{suffix}"),
                new NpgsqlParameter("accountId", $"account-{suffix}"));
            firstGameId = await InsertGame(dataSource, platformId, suffix, "First", "one");
            secondGameId = await InsertGame(dataSource, platformId, suffix, "Second", "two");
            var (lowerGameId, higherGameId) = firstGameId < secondGameId
                ? (firstGameId.Value, secondGameId.Value)
                : (secondGameId.Value, firstGameId.Value);
            await Execute(dataSource, """
                insert into game_connections (user_id, game_id, connected_game_id)
                values (@userId, @gameId, @connectedGameId)
                """,
                new NpgsqlParameter("userId", userId.Value),
                new NpgsqlParameter("gameId", lowerGameId),
                new NpgsqlParameter("connectedGameId", higherGameId));
            await Execute(dataSource, """
                insert into play_sessions
                    (platform_credential_id, game_id, started_at, ended_at)
                values (@credentialId, @gameId, '2026-07-18T10:00:00Z', '2026-07-18T11:00:00Z')
                """,
                new NpgsqlParameter("credentialId", credentialId),
                new NpgsqlParameter("gameId", firstGameId.Value));

            await using var db = new PlaydexDbContext(
                new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(dataSource).Options);
            var games = new GamesRepository(db);
            var providers = new ProvidersRepository(db);
            var admin = new AdminRepository(db);

            var game = await games.GetGameById(firstGameId.Value, CancellationToken.None);
            var search = await games.SearchGames(suffix, 10, CancellationToken.None);
            var connections = await games.ListConnections(userId.Value, CancellationToken.None);
            var providerRows = await providers.ListProviders(userId.Value, CancellationToken.None);
            var (sessions, total) = await admin.SearchSessions(
                suffix, 25, 0, CancellationToken.None);
            var credentials = await admin.ListCredentials(CancellationToken.None);

            Assert.NotNull(game);
            Assert.Equal($"https://example.test/{suffix}/one.jpg", game!.CapsuleUrl);
            Assert.Equal(2, search.Count);
            Assert.Single(connections);
            Assert.All(connections, connection =>
            {
                Assert.NotNull(connection.CapsuleUrl);
                Assert.NotNull(connection.ConnectedCapsuleUrl);
            });
            var provider = Assert.Single(providerRows);
            Assert.Equal($"account-{suffix}", provider.AccountId);
            Assert.Equal($"key-{suffix}", provider.ApiKey);
            Assert.Equal(1, total);
            Assert.Equal($"projection-{suffix}@example.com", Assert.Single(sessions).UserEmail);
            Assert.Contains(credentials, row => row.Id == credentialId
                && row.AccountUser == $"account-{suffix}"
                && row.ApiKey == $"key-{suffix}");
        }
        finally
        {
            if (userId.HasValue)
            {
                await Execute(dataSource, "delete from users where id = @id",
                    new NpgsqlParameter("id", userId.Value));
            }

            if (firstGameId.HasValue)
            {
                await Execute(dataSource, "delete from games where id = @id",
                    new NpgsqlParameter("id", firstGameId.Value));
            }

            if (secondGameId.HasValue)
            {
                await Execute(dataSource, "delete from games where id = @id",
                    new NpgsqlParameter("id", secondGameId.Value));
            }
        }
    }

    private static Task<long> InsertGame(
        NpgsqlDataSource dataSource,
        long platformId,
        string suffix,
        string label,
        string image)
    {
        return Scalar<long>(dataSource, """
            insert into games (platform_id, title, canonical_key, capsule_url)
            values (@platformId, @title, @canonicalKey, @capsuleUrl)
            returning id
            """,
            new NpgsqlParameter("platformId", platformId),
            new NpgsqlParameter("title", $"{label} projection {suffix}"),
            new NpgsqlParameter("canonicalKey", $"projection:{suffix}:{image}"),
            new NpgsqlParameter("capsuleUrl", $"https://example.test/{suffix}/{image}.jpg"));
    }

    private static async Task<T> Scalar<T>(
        NpgsqlDataSource dataSource,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Execute(
        NpgsqlDataSource dataSource,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
