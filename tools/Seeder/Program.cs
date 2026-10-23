using Playdex.Persistence;
using Microsoft.EntityFrameworkCore;
using Seeder;

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "postgresql://playdex:playdex@db:5432/playdex";

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("DATABASE_URL is not set");
}

// The seeder truncates every table. Only local and compose databases are fair game unless SEEDER_FORCE=1 says so.
var host = new Npgsql.NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalize(connectionString)).Host ?? "";
string[] localHosts = ["localhost", "127.0.0.1", "::1", "db"];
if (!localHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
    && Environment.GetEnvironmentVariable("SEEDER_FORCE") != "1")
{
    Console.Error.WriteLine($"Refusing to wipe the database on '{host}'. The seeder truncates every table; set SEEDER_FORCE=1 if that is really what you want.");
    return 1;
}

var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(
    PostgresConnectionString.Normalize(connectionString));
dataSourceBuilder.MapEnum<GameExternalIdentityType>("game_external_identity_type");
dataSourceBuilder.MapEnum<IntegrationType>("integration_type");
dataSourceBuilder.MapEnum<CaptureMethod>("capture_method");
await using var dataSource = dataSourceBuilder.Build();

var options = new DbContextOptionsBuilder<PlaydexDbContext>()
    .UseNpgsql(
        dataSource,
        npgsql =>
        {
            npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            npgsql.MapEnum<IntegrationType>("integration_type");
            npgsql.MapEnum<CaptureMethod>("capture_method");
        })
    .AddInterceptors(new TimestampInterceptor(DateTimeOffset.UtcNow))
    .Options;

var seeder = new DatabaseSeeder(new PlaydexDbContext(options));
await seeder.Seed();
return 0;
