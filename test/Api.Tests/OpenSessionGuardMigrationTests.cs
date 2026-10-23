using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class OpenSessionGuardMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EndsOpenDuplicatesBeforeGuardingThem()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var suffix = Guid.NewGuid().ToString("n");
        await Execute(connection, transaction, """
            drop index play_sessions_open_polling_key;
            drop index play_sessions_open_api_key;
            drop index play_sessions_open_manual_key;
            """);
        var userId = (await Scalars(connection, transaction,
            $"insert into users (email) values ('migration-{suffix}@example.test') returning id;"))[0];
        var games = await Scalars(connection, transaction,
            "insert into games (title) values ('Migration A'), ('Migration B') returning id;");
        var (gameA, gameB) = (games[0], games[1]);
        var integrations = await Scalars(connection, transaction, $"""
            insert into integrations (user_id, type, platform_id, active, created_at, updated_at)
            values ({userId}, 'steam', (select id from platforms where key = 'steam'), true, now(), now()),
                   ({userId}, 'api', null, true, now(), now())
            returning id;
            """);
        var (steam, api) = (integrations[0], integrations[1]);
        // Each group holds an older open duplicate; the API integration also has a lone open session of game B.
        var sessions = await Scalars(connection, transaction, $"""
            insert into play_sessions
                (user_id, game_id, platform_id, capture_method, integration_id, started_at, ended_at, created_at, updated_at)
            select {userId}, game_id, (select id from platforms where key = 'steam'), method::capture_method,
                   integration_id, started_at, null, started_at, updated_at
            from (values
                (1, {gameA}, 'polling', {steam}, timestamptz '2026-10-01 10:00Z', timestamptz '2026-10-01 10:30Z'),
                (2, {gameB}, 'polling', {steam}, timestamptz '2026-10-01 10:20Z', timestamptz '2026-10-01 11:00Z'),
                (3, {gameA}, 'api', {api}, timestamptz '2026-10-01 12:00Z', timestamptz '2026-10-01 12:30Z'),
                (4, {gameA}, 'api', {api}, timestamptz '2026-10-01 13:00Z', timestamptz '2026-10-01 13:05Z'),
                (5, {gameB}, 'api', {api}, timestamptz '2026-10-01 12:10Z', timestamptz '2026-10-01 12:40Z'),
                (6, {gameA}, 'manual', null, timestamptz '2026-10-01 14:00Z', timestamptz '2026-10-01 14:00Z'),
                (7, {gameB}, 'manual', null, timestamptz '2026-10-01 15:00Z', timestamptz '2026-10-01 15:00Z'))
                as rows(position, game_id, method, integration_id, started_at, updated_at)
            order by position
            returning id;
            """);

        await new OpenSessionGuardMigration().ApplyAsync(connection, transaction, default);

        var endedAt = await EndTimes(connection, transaction, sessions);
        Assert.Equal(
            [
                Utc("2026-10-01 10:20"), null,
                Utc("2026-10-01 12:30"), null,
                null,
                Utc("2026-10-01 15:00"), null,
            ],
            endedAt);
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Scalars(connection, transaction, $"""
            insert into play_sessions
                (user_id, game_id, platform_id, capture_method, integration_id, started_at, created_at, updated_at)
            values ({userId}, {gameB}, (select id from platforms where key = 'steam'), 'api', {api}, now(), now(), now())
            returning id;
            """));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
    }

    private static DateTimeOffset? Utc(string value) =>
        DateTimeOffset.Parse($"{value}Z", System.Globalization.CultureInfo.InvariantCulture);

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<long>> Scalars(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<long>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetInt64(0));
        }

        return values;
    }

    private static async Task<List<DateTimeOffset?>> EndTimes(
        NpgsqlConnection connection, NpgsqlTransaction transaction, List<long> sessionIds)
    {
        await using var command = new NpgsqlCommand(
            "select ended_at from play_sessions where id = any(@ids) order by id", connection, transaction);
        command.Parameters.AddWithValue("ids", sessionIds.ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        var values = new List<DateTimeOffset?>();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? null : reader.GetFieldValue<DateTimeOffset>(0));
        }

        return values;
    }
}
