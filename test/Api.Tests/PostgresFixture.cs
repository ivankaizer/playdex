using Npgsql;
using Api.Shared.Persistence;
using Xunit;

namespace Api.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "PostgreSQL integration";
}

public sealed class PostgresFixture : IAsyncLifetime
{
    private const string EnvironmentVariable = "PLAYDEX_INTEGRATION_DATABASE_URL";

    public NpgsqlDataSource? DataSource { get; private set; }

    public bool IsConfigured => DataSource is not null;

    public async Task InitializeAsync()
    {
        var databaseUrl = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return;
        }

        DataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        await using var connection = await DataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("select 1", connection);
        await command.ExecuteScalarAsync();
    }

    public async Task DisposeAsync()
    {
        if (DataSource is not null)
        {
            await DataSource.DisposeAsync();
        }
    }
}
