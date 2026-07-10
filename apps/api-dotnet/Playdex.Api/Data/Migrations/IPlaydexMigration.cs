using Npgsql;

namespace Playdex.Api.Data.Migrations;

public interface IPlaydexMigration
{
    string Id { get; }
    bool SupportsRollback { get; }

    Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}
