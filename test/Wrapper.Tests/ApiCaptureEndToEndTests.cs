using Playdex.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Wrapper;

namespace Wrapper.Tests;

[CollectionDefinition(WrapperPostgresCollectionDefinition.Name, DisableParallelization = true)]
public sealed class WrapperPostgresCollectionDefinition : ICollectionFixture<WrapperPostgresFixture>
{
    public const string Name = "Wrapper PostgreSQL integration";
}

public sealed class WrapperPostgresFixture : IAsyncLifetime
{
    public NpgsqlDataSource? DataSource { get; private set; }
    public bool IsConfigured => DataSource is not null;

    public async Task InitializeAsync()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAY" + "DEX_INTEGRATION_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl)) return;

        var builder = new NpgsqlDataSourceBuilder(PostgresConnectionString.Normalize(databaseUrl));
        builder.MapEnum<GameExternalIdentityType>("game_external_identity_type");
        builder.MapEnum<IntegrationType>("integration_type");
        builder.MapEnum<CaptureMethod>("capture_method");
        DataSource = builder.Build();
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null) await DataSource.DisposeAsync();
    }
}

[Collection(WrapperPostgresCollectionDefinition.Name)]
public sealed class ApiCaptureEndToEndTests(WrapperPostgresFixture postgres)
{
    [Fact]
    public async Task WrapperPingUsesLiveGenericCaptureContract()
    {
        if (!postgres.IsConfigured) return;

        ConfigureHost();
        await using var factory = new WebApplicationFactory<global::Program>();
        using var startupClient = factory.CreateClient();
        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        var suffix = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;
        var apiKey = $"key-{suffix}";
        var userId = (long)(await new NpgsqlCommand("""
            insert into users(email, created_at, updated_at)
            values ($1, $2, $2)
            returning id
            """, connection)
        {
            Parameters =
            {
                new() { Value = $"wrapper-capture-{suffix}@example.test" },
                new() { Value = now },
            }
        }.ExecuteScalarAsync())!;
        var integrationId = (long)(await new NpgsqlCommand("""
            insert into integrations(user_id, type, api_key, active, created_at, updated_at)
            values ($1, 'api', $2, true, $3, $3)
            returning id
            """, connection)
        {
            Parameters =
            {
                new() { Value = userId },
                new() { Value = apiKey },
                new() { Value = now },
            }
        }.ExecuteScalarAsync())!;

        try
        {
            using var logging = AppLoggerFactory.CreateInDirectory(TestPaths.NewTempDir());
            using var api = new PingApiClient(
                new ApiConfig
                {
                    BaseUrl = "http://localhost/api",
                    ApiKey = apiKey,
                    Platform = "pc",
                },
                new GameConfig
                {
                    GameId = $"wrapper-{suffix}",
                    ApiGameId = $"app-{suffix}",
                    DisplayName = "Wrapper contract game",
                    GameProvider = "steam",
                    Platform = "gog",
                },
                logging.CreateLogger<PingApiClient>(),
                factory.Server.CreateHandler());

            Assert.True(await api.PingAsync(CancellationToken.None));

            await using var assertion = new NpgsqlCommand("""
                select p.key, gei.type::text, gei.external_id
                from play_sessions ps
                join platforms p on p.id = ps.platform_id
                join game_external_identities gei on gei.game_id = ps.game_id
                where ps.integration_id = $1 and ps.ended_at is null
                """, connection);
            assertion.Parameters.AddWithValue(integrationId);
            await using var reader = await assertion.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("gog", reader.GetString(0));
            Assert.Equal("steam", reader.GetString(1));
            Assert.Equal($"app-{suffix}", reader.GetString(2));
            Assert.True(!await reader.ReadAsync());
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand("""
                delete from play_sessions where integration_id = $1;
                delete from integrations where id = $1;
                with deleted_identity as (
                    delete from game_external_identities where external_id = $2 returning game_id
                )
                delete from games where id in (select game_id from deleted_identity);
                delete from users where id = $3;
                """, connection);
            cleanup.Parameters.AddWithValue(integrationId);
            cleanup.Parameters.AddWithValue($"app-{suffix}");
            cleanup.Parameters.AddWithValue(userId);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    private static void ConfigureHost()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAY" + "DEX_INTEGRATION_DATABASE_URL")!;
        Environment.SetEnvironmentVariable("DATABASE_URL", databaseUrl);
        Environment.SetEnvironmentVariable(
            "Auth__JwtSecret",
            "wrapper-capture-integration-secret-at-least-thirty-two-bytes");
        Environment.SetEnvironmentVariable("POLL_INTERVAL_SECONDS", "3600");
    }
}
