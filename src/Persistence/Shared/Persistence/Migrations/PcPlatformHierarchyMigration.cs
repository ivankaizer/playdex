using Npgsql;

namespace Shared.Persistence.Migrations;

public sealed class PcPlatformHierarchyMigration : IPlaydexMigration
{
    public string Id => "011_pc_platform_hierarchy";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        update platforms
        set parent_id = (select id from platforms where key = 'pc'),
            updated_at = now()
        where key = 'gog'
          and parent_id is distinct from (select id from platforms where key = 'pc');

        update platforms
        set parent_id = null,
            updated_at = now()
        where key = 'playnite'
          and parent_id is not null;
        """;
}
