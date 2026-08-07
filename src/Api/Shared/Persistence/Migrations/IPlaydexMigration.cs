using Npgsql;

namespace Api.Shared.Persistence.Migrations;

public interface IPlaydexMigration
{
    string Id { get; }
    bool SupportsRollback { get; }

    Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken);
}
