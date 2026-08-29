using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>Corrects legacy Playnite sessions that inherited a Game catalog platform.</summary>
public sealed class CorrectLegacyApiSessionPlatformsMigration : IPlaydexMigration
{
    public string Id => "010_correct_legacy_api_session_platforms";
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
        update play_sessions ps
        set platform_id = pc.id,
            updated_at = now()
        from integrations i
        cross join platforms pc
        cross join dotnet_schema_migrations legacy_drop
        where ps.integration_id = i.id
          and i.type = 'api'
          and ps.capture_method = 'api'
          and ps.created_at <= legacy_drop.applied_at
          and pc.key = 'pc'
          and legacy_drop.migration_id = '009_drop_legacy_model'
          and ps.platform_id <> pc.id;
        """;
}
