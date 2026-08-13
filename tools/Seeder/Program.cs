using Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Seeder;

var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? "postgresql://playdex:playdex@db:5432/playdex";

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("DATABASE_URL is not set");
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
