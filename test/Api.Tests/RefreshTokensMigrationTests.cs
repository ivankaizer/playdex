using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class RefreshTokensMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task EmailsBecomeUniqueRegardlessOfCase()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetUpSchema(connection, transaction);
        await Execute(connection, transaction, "insert into users (email) values ('player@example.com');");

        await new RefreshTokensMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            Execute(connection, transaction, "insert into users (email) values ('Player@Example.com');"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task AccountsThatDifferOnlyInEmailCaseStopTheMigrationUntouched()
    {
        if (!postgres.IsConfigured) return;

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await SetUpSchema(connection, transaction);
        await Execute(connection, transaction, """
            insert into users (email)
            values ('Player@example.com'), ('player@example.com'), ('other@example.com');
            """);

        var exception = await Assert.ThrowsAsync<PostgresException>(() =>
            new RefreshTokensMigration().ApplyAsync(connection, transaction, CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Contains("player@example.com", exception.MessageText);
        Assert.DoesNotContain("other@example.com", exception.MessageText);
        Assert.Contains("merge or rename", exception.MessageText);
    }

    private static async Task SetUpSchema(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        var schema = new NpgsqlCommandBuilder().QuoteIdentifier($"refresh_tokens_{Guid.NewGuid():N}");
        await Execute(connection, transaction, $"create schema {schema}; set local search_path to {schema};");
        await Execute(connection, transaction, """
            create table users (id bigserial primary key, email text not null unique);
            """);
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }
}
