using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Runs <see cref="DurableNotificationsAndBackoffMigration"/> again inside a transaction that is rolled back, against
/// the migrated integration database.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class DurableNotificationsAndBackoffMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MigrationIsRepeatable()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await new DurableNotificationsAndBackoffMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        await new DurableNotificationsAndBackoffMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        Assert.True(await Scalar<bool>(connection, transaction, """
            select count(*) = 2 from information_schema.columns
            where table_schema = current_schema() and table_name = 'integrations'
              and column_name in ('paused_reason', 'paused_at');
            """));
        Assert.True(await Scalar<bool>(connection, transaction,
            "select to_regclass('notification_outbox_pending_idx') is not null;"));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task BackoffGoesWithItsIntegration()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var integrationId = await InsertSteamIntegration(connection, transaction);
        await Execute(connection, transaction, $"""
            insert into integration_poll_backoff (integration_id, failures, next_poll_at, updated_at)
            values ({integrationId}, 3, now(), now());
            delete from integrations where id = {integrationId};
            """);

        Assert.Equal(0L, await Scalar<long>(connection, transaction,
            $"select count(*) from integration_poll_backoff where integration_id = {integrationId};"));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task BackoffCountsCannotGoNegative()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var integrationId = await InsertSteamIntegration(connection, transaction);

        var exception = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, transaction, $"""
            insert into integration_poll_backoff (integration_id, failures, next_poll_at, updated_at)
            values ({integrationId}, -1, now(), now());
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Equal("integration_poll_backoff_failures_check", exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AnEventIsQueuedOnce()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var key = $"migration-test:{Guid.NewGuid():n}";
        const string insert = """
            insert into notification_outbox (dedupe_key, kind, payload, next_attempt_at, created_at)
            values (@key, 'SessionStarted', '{}', now(), now());
            """;
        await using (var first = new NpgsqlCommand(insert, connection, transaction))
        {
            first.Parameters.AddWithValue("key", key);
            await first.ExecuteNonQueryAsync();
        }

        await using var second = new NpgsqlCommand(insert, connection, transaction);
        second.Parameters.AddWithValue("key", key);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => second.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal("notification_outbox_dedupe_key_unique", exception.ConstraintName);
        await transaction.RollbackAsync();
    }

    private static Task<long> InsertSteamIntegration(NpgsqlConnection connection, NpgsqlTransaction transaction) =>
        Scalar<long>(connection, transaction, $"""
            with new_user as (
                insert into users (email) values ('backoff-{Guid.NewGuid():n}@example.test') returning id)
            insert into integrations (user_id, type, platform_id, api_key, account_id)
            select id, 'steam', (select id from platforms where key = 'steam'), 'key', '76561190000000009'
            from new_user
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
