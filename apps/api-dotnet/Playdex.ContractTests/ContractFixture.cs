using System.Reflection;
using Dapper;
using Npgsql;
using Xunit;

namespace Playdex.ContractTests;

/// <summary>
/// Seeds an isolated PostgreSQL database with identical fixture data that
/// both the Bun reference API and the .NET candidate API query.
///
/// Skipped entirely when <c>PLAYDEX_CONTRACT_DATABASE_URL</c> is not set,
/// matching the existing integration-test convention.
/// </summary>
public sealed class ContractFixture : IAsyncLifetime
{
    /// <summary>The database URL that was used for seeding.</summary>
    public string DatabaseUrl { get; }

    /// <summary>Email of the seeded test user.</summary>
    public string TestUserEmail { get; } = "contract-test@playdex.app";

    /// <summary>Known plain-text password for the test user.</summary>
    public string TestUserPassword { get; } = "contract-test-password";

    /// <summary>Primary-key ID of the seeded test user.</summary>
    public long TestUserId { get; } = 100;

    /// <summary>True when the database URL was available and seeding succeeded.</summary>
    public bool IsAvailable { get; }

    private readonly NpgsqlDataSource _dataSource;

    public ContractFixture()
    {
        var databaseUrl = Environment.GetEnvironmentVariable("PLAYDEX_CONTRACT_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            IsAvailable = false;
            DatabaseUrl = null!;
            _dataSource = null!;
            return;
        }

        DatabaseUrl = databaseUrl;
        _dataSource = new NpgsqlDataSourceBuilder(
            PostgresConnectionString.Normalize(databaseUrl)).Build();
        IsAvailable = true;
    }

    public async Task InitializeAsync()
    {
        if (!IsAvailable)
            return;

        await using var connection = await _dataSource.OpenConnectionAsync();

        // 1. Load and execute the embedded seed SQL
        var seedSql = LoadEmbeddedSeedSql();
        await connection.ExecuteAsync(seedSql);

        // 2. Hash the known password and write it into the user record.
        //    Both Bun (bcryptjs) and .NET (BCrypt.Net) can verify bcrypt hashes
        //    with the $2a$ / $2b$ prefix, so the same credential works on both
        //    sides.
        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(TestUserPassword, workFactor: 10);
        await connection.ExecuteAsync(
            "update users set password = @hash where id = @id",
            new { hash = hashedPassword, id = TestUserId });
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync();
    }

    /// <summary>Connects to the seeded database and returns an open <see cref="NpgsqlConnection"/>.</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = _dataSource.CreateConnection();
        await connection.OpenAsync();
        return connection;
    }

    private static string LoadEmbeddedSeedSql()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Playdex.ContractTests.SeedData.seed.sql";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found. " +
                "Ensure SeedData/seed.sql has build action EmbeddedResource.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
