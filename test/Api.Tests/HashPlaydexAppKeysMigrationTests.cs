using Api.Infrastructure.Auth;
using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Runs <see cref="HashPlaydexAppKeysMigration"/> again inside a transaction that is rolled back, against the migrated
/// integration database, with app keys stored the way the previous release stored them.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class HashPlaydexAppKeysMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task PlaintextKeysAreHashedTheWayTheApiHashesThemAndSharedOnesDropped()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await Execute(connection, transaction, "drop index if exists integrations_app_key_unique;");
        var suffix = Guid.NewGuid().ToString("n");
        var userId = await Scalar<long>(connection, transaction,
            $"insert into users (email) values ('app-keys-{suffix}@example.test') returning id;");
        var own = await InsertAppKey(connection, transaction, userId, $"plx_own_{suffix}");
        var sharedA = await InsertAppKey(connection, transaction, userId, $"plx_shared_{suffix}");
        var sharedB = await InsertAppKey(connection, transaction, userId, $"plx_shared_{suffix}");
        var steam = await Scalar<long>(connection, transaction, $"""
            insert into integrations (user_id, type, platform_id, api_key, account_id)
            values ({userId}, 'steam', (select id from platforms where key = 'steam'), 'enc:v1:unchanged', '76561190000000201')
            returning id;
            """);

        await new HashPlaydexAppKeysMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        await new HashPlaydexAppKeysMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        Assert.Equal(PlaydexAppKeys.Hash($"plx_own_{suffix}"), await StoredKey(connection, transaction, own));
        Assert.Null(await StoredKey(connection, transaction, sharedA));
        Assert.Null(await StoredKey(connection, transaction, sharedB));
        Assert.Equal("enc:v1:unchanged", await StoredKey(connection, transaction, steam));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertAppKey(connection, transaction, userId, PlaydexAppKeys.Hash($"plx_own_{suffix}")));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        Assert.Equal("integrations_app_key_unique", duplicate.ConstraintName);
        await transaction.RollbackAsync();
    }

    private static Task<long> InsertAppKey(NpgsqlConnection connection, NpgsqlTransaction transaction, long userId, string apiKey) =>
        Scalar<long>(connection, transaction, $"""
            insert into integrations (user_id, type, api_key, account_id)
            values ({userId}, 'api', '{apiKey}', 'api')
            returning id;
            """);

    private static async Task<string?> StoredKey(NpgsqlConnection connection, NpgsqlTransaction transaction, long id)
    {
        await using var command = new NpgsqlCommand($"select api_key from integrations where id = {id};", connection, transaction);
        return await command.ExecuteScalarAsync() as string;
    }

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
