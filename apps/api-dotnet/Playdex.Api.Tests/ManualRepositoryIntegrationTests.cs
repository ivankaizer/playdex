using Microsoft.EntityFrameworkCore;
using Npgsql;
using Playdex.Api.Data;
using Playdex.Api.Manual;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class ManualRepositoryIntegrationTests
{
    [Fact]
    public async Task MapManualInputScopesUpdateToAuthenticatedUser()
    {
        var databaseUrl = RequireDatabaseUrl();
        await using var dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        var suffix = Guid.NewGuid().ToString("N");
        var ownerId = await Scalar<long>(dataSource,
            "insert into users (email) values (@email) returning id",
            new NpgsqlParameter("email", $"manual-owner-{suffix}@example.com"));
        var otherId = await Scalar<long>(dataSource,
            "insert into users (email) values (@email) returning id",
            new NpgsqlParameter("email", $"manual-other-{suffix}@example.com"));
        var platformId = await Scalar<long>(dataSource,
            "select id from platforms where key = 'manual'");
        var gameId = await Scalar<long>(dataSource, """
            insert into games (platform_id, title, canonical_key)
            values (@platformId, @title, @canonicalKey)
            returning id
            """,
            new NpgsqlParameter("platformId", platformId),
            new NpgsqlParameter("title", $"Manual security {suffix}"),
            new NpgsqlParameter("canonicalKey", $"manual-security:{suffix}"));
        var ownerInputId = await InsertInput(dataSource, ownerId, suffix, "owner");
        var otherInputId = await InsertInput(dataSource, otherId, suffix, "other");

        try
        {
            await using var db = new PlaydexDbContext(
                new DbContextOptionsBuilder<PlaydexDbContext>()
                    .UseNpgsql(dataSource)
                    .Options);
            var repository = new ManualRepository(db);

            var foreign = await repository.MapManualInput(ownerId, otherInputId, gameId, CancellationToken.None);
            var own = await repository.MapManualInput(ownerId, ownerInputId, gameId, CancellationToken.None);

            Assert.Null(foreign);
            Assert.NotNull(own);
            Assert.Equal(gameId, own!.MappedGameId);
            Assert.Null(await Scalar<long?>(dataSource,
                "select mapped_game_id from manual_game_inputs where id = @id",
                new NpgsqlParameter("id", otherInputId)));
        }
        finally
        {
            await Execute(dataSource, "delete from users where id = any(@ids)",
                new NpgsqlParameter("ids", new[] { ownerId, otherId }));
            await Execute(dataSource, "delete from games where id = @id",
                new NpgsqlParameter("id", gameId));
        }
    }

    private static Task<long> InsertInput(
        NpgsqlDataSource dataSource,
        long userId,
        string suffix,
        string label) =>
        Scalar<long>(dataSource, """
            insert into manual_game_inputs (user_id, raw_title, canonical_title)
            values (@userId, @rawTitle, @canonicalTitle)
            returning id
            """,
            new NpgsqlParameter("userId", userId),
            new NpgsqlParameter("rawTitle", $"{label} {suffix}"),
            new NpgsqlParameter("canonicalTitle", $"{label}-{suffix}"));

    private static string RequireDatabaseUrl() =>
        Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL")
        ?? throw new InvalidOperationException(
            "PLAYDEX_INTEGRATION_DATABASE_URL is required for PostgreSQL integration tests.");

    private static async Task<T> Scalar<T>(
        NpgsqlDataSource dataSource,
        string sql,
        params NpgsqlParameter[] parameters)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        var value = await command.ExecuteScalarAsync();
        if (value is null or DBNull) return default!;
        return (T)Convert.ChangeType(value, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
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
