using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// A per-user token version. Every token carries the version it was issued with; bumping it (a new password)
/// revokes all of the user's refresh tokens.
/// </summary>
public sealed class UserTokenVersionMigration : IPlaydexMigration
{
    public string Id => "015_user_token_version";
    public bool SupportsRollback => true;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "alter table users add column if not exists token_version integer not null default 0;",
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
