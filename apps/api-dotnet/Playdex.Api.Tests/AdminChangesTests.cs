using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Modules.Admin;
using Playdex.Api.Shared.Persistence;
using Playdex.Api.Shared.Http;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class AdminChangesTests
{
    [Fact]
    public void CredentialParserDistinguishesMissingFromExplicitNull()
    {
        using var empty = JsonDocument.Parse("{}");
        using var clearing = JsonDocument.Parse("""{"apiKey":null,"user":"   "}""");

        var unchanged = AdminEndpoints.ParseCredentialChanges(empty.RootElement);
        var cleared = AdminEndpoints.ParseCredentialChanges(clearing.RootElement);

        Assert.False(unchanged.ApiKeyChanged);
        Assert.False(unchanged.AccountUserChanged);
        Assert.True(cleared.ApiKeyChanged);
        Assert.True(cleared.AccountUserChanged);
        Assert.Null(cleared.ApiKey);
        Assert.Null(cleared.AccountUser);
    }

    [Fact]
    public void GameParserPreservesPresenceAndValidatesTypes()
    {
        using var payload = JsonDocument.Parse(
            """{"title":"  New title  ","imageUrl":null,"lockImage":false}""");
        var changes = AdminEndpoints.ParseGameChanges(payload.RootElement);

        Assert.True(changes.TitleChanged);
        Assert.Equal("New title", changes.Title);
        Assert.True(changes.ImageUrlChanged);
        Assert.Null(changes.ImageUrl);
        Assert.False(changes.LockTitleChanged);
        Assert.True(changes.LockImageChanged);
        Assert.False(changes.LockImage);

        using var invalid = JsonDocument.Parse("""{"lockTitle":"yes"}""");
        var problem = Assert.Throws<AppProblem>(
            () => AdminEndpoints.ParseGameChanges(invalid.RootElement));
        Assert.Equal("lockTitle must be a boolean.", problem.Message);
    }

    [Fact]
    public async Task RepositoryCanClearCredentialAndGameImageFields()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")
            ?? throw new InvalidOperationException("PLAYDEX_INTEGRATION_DATABASE_URL is required.");
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("N");
        var userId = await Scalar<long>(dataSource,
            "insert into users (email) values (@email) returning id",
            new NpgsqlParameter("email", $"admin-clear-{suffix}@example.com"));
        var platformId = await Scalar<long>(dataSource, "select id from platforms where key = 'steam'");
        var credentialId = await Scalar<long>(dataSource, """
            insert into platform_credentials (user_id, platform_id, api_key, account_id)
            values (@userId, @platformId, 'secret', 'player') returning id
            """,
            new NpgsqlParameter("userId", userId),
            new NpgsqlParameter("platformId", platformId));
        var gameId = await Scalar<long>(dataSource, """
            insert into games (platform_id, title, canonical_key, capsule_url)
            values (@platformId, 'Clear image', @canonicalKey, 'https://example.test/cover.jpg') returning id
            """,
            new NpgsqlParameter("platformId", platformId),
            new NpgsqlParameter("canonicalKey", $"admin-clear:{suffix}"));

        try
        {
            await using var db = new PlaydexDbContext(
                new DbContextOptionsBuilder<PlaydexDbContext>().UseNpgsql(dataSource).Options);
            var repository = new AdminRepository(db);

            var credential = await repository.UpdateCredential(
                credentialId, true, null, true, null, CancellationToken.None);
            var game = await repository.UpdateGame(
                gameId,
                titleChanged: false,
                title: string.Empty,
                imageUrlChanged: true,
                imageUrl: null,
                lockTitleChanged: false,
                lockTitle: false,
                lockImageChanged: true,
                lockImage: false,
                ct: CancellationToken.None);

            Assert.NotNull(credential);
            Assert.Null(credential!.ApiKey);
            Assert.Null(credential.AccountUser);
            Assert.NotNull(game);
            Assert.Null(game!.CapsuleUrl);
            Assert.False(game.CapsuleUrlLocked);
        }
        finally
        {
            await Execute(dataSource, "delete from users where id = @id", new NpgsqlParameter("id", userId));
            await Execute(dataSource, "delete from games where id = @id", new NpgsqlParameter("id", gameId));
        }
    }

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
