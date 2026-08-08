using Npgsql;

namespace Api.Shared.Persistence.Migrations;

/// <summary>
/// Adds an admin-managed lock flag so a credential's API key cannot be
/// viewed or changed by the user.
/// </summary>
public sealed class CredentialApiKeyLockMigration : IPlaydexMigration
{
    public string Id => "002_credential_api_key_lock";
    public bool SupportsRollback => true;

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            alter table platform_credentials
                add column if not exists api_key_locked boolean not null default false;
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
