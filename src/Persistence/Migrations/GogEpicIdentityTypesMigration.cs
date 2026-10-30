using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Adds GOG and Epic game ids, which the Windows app records games from those launchers with (a GOG game id, an Epic
/// app name). A new enum value can't be used in the transaction that adds it; nothing here uses it.
/// </summary>
public sealed class GogEpicIdentityTypesMigration : IPlaydexMigration
{
    public string Id => "022_gog_epic_identity_types";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            alter type game_external_identity_type add value if not exists 'gog';
            alter type game_external_identity_type add value if not exists 'epic';
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
