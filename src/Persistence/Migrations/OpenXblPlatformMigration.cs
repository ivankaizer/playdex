using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Registers the experimental Xbox provider backed by OpenXBL.
/// </summary>
public sealed class OpenXblPlatformMigration : IPlaydexMigration
{
    public string Id => "003_openxbl_platform";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            insert into platforms (key, name, parent_id)
            values (
                'xbox',
                'Xbox',
                (select id from platforms where key = 'xbox-series-xs'))
            on conflict (key) do update set
                name = excluded.name,
                parent_id = excluded.parent_id,
                updated_at = now();
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
