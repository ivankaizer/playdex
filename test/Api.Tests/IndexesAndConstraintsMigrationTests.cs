using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Runs <see cref="IndexesAndConstraintsMigration"/> again inside a transaction that is rolled back, against the
/// migrated integration database.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class IndexesAndConstraintsMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task DuplicateAccountsStopTheMigrationAndNameTheRows()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction, "drop index if exists integrations_user_type_account_unique;");
        var userId = await InsertUser(connection, transaction);
        var first = await InsertSteamIntegration(connection, transaction, userId, "76561190000000001");
        var second = await InsertSteamIntegration(connection, transaction, userId, "76561190000000001");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            new IndexesAndConstraintsMigration().ApplyAsync(connection, transaction, CancellationToken.None));

        Assert.Contains(IndexesAndConstraintsMigration.DuplicateIntegrationsMessage, exception.MessageText);
        Assert.Contains($"integration ids {first}, {second}", exception.MessageText);
        Assert.NotNull(exception.Hint);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task MigrationIsRepeatableAndRejectsDuplicateAccounts()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await new IndexesAndConstraintsMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        var userId = await InsertUser(connection, transaction);
        await InsertSteamIntegration(connection, transaction, userId, "76561190000000002");

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertSteamIntegration(connection, transaction, userId, "76561190000000002"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("integrations_user_type_account_unique", exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ApiIntegrationsMayShareTheirPlaceholderAccount()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction, "drop index if exists integrations_user_type_account_unique;");
        var userId = await InsertUser(connection, transaction);
        await Execute(connection, transaction, $"""
            insert into integrations (user_id, type, api_key, account_id)
            values ({userId}, 'api', 'plx_first', 'api'), ({userId}, 'api', 'plx_second', 'api');
            """);

        await new IndexesAndConstraintsMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        Assert.True(await Scalar<bool>(connection, transaction,
            "select to_regclass('integrations_user_type_account_unique') is not null;"));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DeletingAGameWithSessionsFailsInsteadOfErasingThem()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var userId = await InsertUser(connection, transaction);
        var gameId = await Scalar<long>(connection, transaction,
            "insert into games (title) values ('Restrict Test') returning id;");
        await Execute(connection, transaction, $"""
            insert into play_sessions (user_id, game_id, platform_id, capture_method, started_at, ended_at)
            values ({userId}, {gameId}, (select id from platforms where key = 'pc'), 'manual', now() - interval '1 hour', now());
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute(connection, transaction, $"delete from games where id = {gameId};"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal("play_sessions_game_id_fkey", exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task DuplicatePlatformKeyIndexIsDroppedOnlyWhenAnotherUniqueIndexRemains()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction,
            "create unique index if not exists platforms_key_unique on platforms (key);");

        await new IndexesAndConstraintsMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        Assert.False(await PlatformsKeyUniqueExists(connection, transaction));

        await Execute(connection, transaction, """
            alter table platforms drop constraint platforms_key_key;
            create unique index platforms_key_unique on platforms (key);
            """);
        await new IndexesAndConstraintsMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        Assert.True(await PlatformsKeyUniqueExists(connection, transaction));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task HotPathIndexesExist()
    {
        if (!postgres.IsConfigured) return;

        await using var command = postgres.DataSource!.CreateCommand("""
            select array_agg(indexname::text order by indexname)
            from pg_indexes
            where schemaname = current_schema()
              and indexname in (
                'play_sessions_game_id_idx', 'play_sessions_started_at_idx', 'play_sessions_open_user_id_idx',
                'play_sessions_ended_at_idx', 'ai_generations_user_id_idx', 'poll_results_integration_id_polled_at_idx',
                'poll_results_integration_id_idx')
            """);

        var indexes = (string[])(await command.ExecuteScalarAsync())!;

        Assert.Equal(
            [
                "ai_generations_user_id_idx",
                "play_sessions_ended_at_idx",
                "play_sessions_game_id_idx",
                "play_sessions_open_user_id_idx",
                "play_sessions_started_at_idx",
                "poll_results_integration_id_polled_at_idx",
            ],
            indexes);
    }

    private static Task<bool> PlatformsKeyUniqueExists(NpgsqlConnection connection, NpgsqlTransaction transaction) =>
        Scalar<bool>(connection, transaction, "select to_regclass('platforms_key_unique') is not null;");

    private static Task<long> InsertUser(NpgsqlConnection connection, NpgsqlTransaction transaction) =>
        Scalar<long>(connection, transaction,
            $"insert into users (email) values ('constraints-{Guid.NewGuid():n}@example.test') returning id;");

    private static Task<long> InsertSteamIntegration(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        long userId,
        string accountId) =>
        Scalar<long>(connection, transaction, $"""
            insert into integrations (user_id, type, platform_id, api_key, account_id)
            values ({userId}, 'steam', (select id from platforms where key = 'steam'), 'key', '{accountId}')
            returning id;
            """);

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
