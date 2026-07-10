using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Playdex.Api.Configuration;
using Playdex.Api.Data;
using Playdex.Api.Http;
using Xunit;

namespace Playdex.Api.Tests;

public sealed class PlaydexOptionsTests
{
    [Fact]
    public void FromConfiguration_RejectsAMissingDatabaseUrl()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        var exception = Assert.Throws<OptionsValidationException>(
            () => PlaydexOptions.FromConfiguration(configuration));

        Assert.Equal("DATABASE_URL is required.", exception.Message);
    }

    [Fact]
    public void FromConfiguration_UsesExistingEnvironmentVariableNames()
    {
        var values = new Dictionary<string, string?>
        {
            ["DATABASE_URL"] = "postgresql://playdex:playdex@localhost:5432/playdex",
            ["JWT_SECRET"] = "unit-test-secret-that-is-long-enough-for-hmac",
            ["POLL_INTERVAL_SECONDS"] = "180",
            ["ACCESS_TOKEN_TTL"] = "30m",
            ["CORS_ORIGINS"] = "http://ui.playdex.orb.local, https://ui.playdex.orb.local",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var options = PlaydexOptions.FromConfiguration(configuration);

        Assert.Equal(values["DATABASE_URL"], options.DatabaseUrl);
        Assert.Equal(values["JWT_SECRET"], options.JwtSecret);
        Assert.Equal(180, options.PollIntervalSeconds);
        Assert.Equal("30m", options.AccessTokenTtl);
        Assert.Equal(
            ["http://ui.playdex.orb.local", "https://ui.playdex.orb.local"],
            options.CorsOrigins);
    }
}


public sealed class AppProblemTests
{
    [Fact]
    public void NotFound_ReturnsTheExistingErrorEnvelope()
    {
        var problem = AppProblem.NotFound("Route not found.");

        Assert.Equal(404, problem.StatusCode);
        Assert.Null(problem.ToEnvelope().Data);
        Assert.Equal("Route not found.", problem.ToEnvelope().Error?.Error);
    }
}

public sealed class ErrorResponseFactoryTests
{
    [Theory]
    [InlineData(400, "Invalid request.")]
    [InlineData(401, "Unauthorized.")]
    [InlineData(403, "Forbidden.")]
    [InlineData(404, "Route not found.")]
    [InlineData(409, "Conflict.")]
    public void FromException_PreservesKnownApplicationErrors(int statusCode, string message)
    {
        var result = ErrorResponseFactory.FromException(new AppProblem(statusCode, message));

        Assert.Equal(statusCode, result.StatusCode);
        Assert.Equal(message, result.Envelope.Error?.Error);
    }

    [Fact]
    public void FromException_HidesUnhandledExceptionDetails()
    {
        var result = ErrorResponseFactory.FromException(new InvalidOperationException("database password leaked"));

        Assert.Equal(500, result.StatusCode);
        Assert.Equal("Internal server error.", result.Envelope.Error?.Error);
    }
}

public sealed class PlaydexDbContextTests
{
    [Fact]
    public void Model_MapsEveryExistingApplicationTable()
    {
        var options = new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql("postgresql://playdex:playdex@localhost:5432/playdex")
            .Options;
        using var context = new PlaydexDbContext(options);

        var tableNames = context.Model.GetEntityTypes()
            .Select(entity => entity.GetTableName()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "game_connections",
                "game_ratings",
                "games",
                "ignored_games",
                "manual_game_inputs",
                "platform_credentials",
                "platforms",
                "play_sessions",
                "poll_results",
                "user_settings",
                "users",
            ],
            tableNames);
    }

    [Fact]
    public async Task ExistingDatabase_AllMappedTablesAreReadableWithoutChangingMigrationHistory()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_INTEGRATION_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        var options = new DbContextOptionsBuilder<PlaydexDbContext>()
            .UseNpgsql(PostgresConnectionString.Normalize(databaseUrl))
            .Options;
        await using var context = new PlaydexDbContext(options);
        var historyBefore = await ReadMigrationHistoryCounts(context);

        _ = await context.Users.CountAsync();
        _ = await context.Platforms.CountAsync();
        _ = await context.PlatformCredentials.CountAsync();
        _ = await context.Games.CountAsync();
        _ = await context.PlaySessions.CountAsync();
        _ = await context.PollResults.CountAsync();
        _ = await context.ManualGameInputs.CountAsync();
        _ = await context.IgnoredGames.CountAsync();
        _ = await context.GameRatings.CountAsync();
        _ = await context.UserSettings.CountAsync();
        _ = await context.GameConnections.CountAsync();

        Assert.Equal(historyBefore, await ReadMigrationHistoryCounts(context));
    }

    private static async Task<(long Legacy, long Kysely)> ReadMigrationHistoryCounts(PlaydexDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "select (select count(*) from schema_migrations), (select count(*) from kysely_migration)";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }
}

public sealed class PostgresConnectionStringTests
{
    [Fact]
    public void Normalize_PreservesTheExistingDatabaseUrlContract()
    {
        var normalized = PostgresConnectionString.Normalize(
            "postgresql://playdex:p%40ss@localhost:5432/playdex");
        var parsed = new Npgsql.NpgsqlConnectionStringBuilder(normalized);

        Assert.Equal("localhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("playdex", parsed.Database);
        Assert.Equal("playdex", parsed.Username);
        Assert.Equal("p@ss", parsed.Password);
    }
}
