using Api.Modules.Games;
using Npgsql;
using Playdex.Persistence;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

/// <summary>
/// Runs <see cref="GogEpicIdentityTypesMigration"/> again inside a transaction that is rolled back, against the migrated
/// integration database.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class GogEpicIdentityTypesMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task GogAndEpicIdsCanBeStoredAndTheMigrationRunsAgain()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await new GogEpicIdentityTypesMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        await new GogEpicIdentityTypesMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        var suffix = Guid.NewGuid().ToString("n");

        await using var command = new NpgsqlCommand($"""
            with game as (insert into games (title) values ('Harbor Lights {suffix}') returning id)
            insert into game_external_identities (game_id, type, external_id, title)
            select id, type::game_external_identity_type, external_id, 'Harbor Lights'
            from game, (values ('gog', '1{suffix[..8]}'), ('epic', 'Harbor{suffix}')) as ids(type, external_id)
            returning type::text;
            """, connection, transaction);
        var types = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                types.Add(reader.GetString(0));
            }
        }

        Assert.Equal(["gog", "epic"], types);
        await transaction.RollbackAsync();
    }

    [Fact]
    public void EveryIdTypeHasTheKeyTheCaptureRoutesTake()
    {
        foreach (var type in Enum.GetValues<GameExternalIdentityType>())
        {
            Assert.Equal(type, GameExternalIdentityTypes.FromKey(type.ToKey()));
        }
        Assert.Equal(GameExternalIdentityType.Gog, GameExternalIdentityTypes.FromKey("gog"));
        Assert.Equal(GameExternalIdentityType.Epic, GameExternalIdentityTypes.FromKey(" EPIC "));
    }
}
