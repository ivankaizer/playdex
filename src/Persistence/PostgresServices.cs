using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Playdex.Persistence;

public static class PostgresServices
{
    public static NpgsqlDataSource CreateDataSource(string databaseUrl)
    {
        var builder = new NpgsqlDataSourceBuilder(PostgresConnectionString.Normalize(databaseUrl));
        builder.MapEnum<GameExternalIdentityType>("game_external_identity_type");
        builder.MapEnum<IntegrationType>("integration_type");
        builder.MapEnum<CaptureMethod>("capture_method");
        return builder.Build();
    }

    public static void AddPlaydexDbContext(this IServiceCollection services, NpgsqlDataSource dataSource)
    {
        services.AddDbContext<PlaydexDbContext>(options => options.UseNpgsql(dataSource, npgsql =>
        {
            npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            npgsql.MapEnum<IntegrationType>("integration_type");
            npgsql.MapEnum<CaptureMethod>("capture_method");
        }));
    }

    public static void AddPlaydexDbContextFactory(this IServiceCollection services, NpgsqlDataSource dataSource)
    {
        services.AddPooledDbContextFactory<PlaydexDbContext>(options => options.UseNpgsql(dataSource, npgsql =>
        {
            npgsql.MapEnum<GameExternalIdentityType>("game_external_identity_type");
            npgsql.MapEnum<IntegrationType>("integration_type");
            npgsql.MapEnum<CaptureMethod>("capture_method");
        }));
    }
}
