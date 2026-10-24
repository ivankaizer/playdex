using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// Stores Playdex app keys as SHA-256 hashes (<c>sha256:&lt;hex&gt;</c>, as <c>PlaydexAppKeys.Hash</c> in the API) and
/// makes them unique. Apps keep working: they send the same key, and the API hashes it before looking it up. Not
/// rollback-safe: the previous release compares keys as stored, so no app could authenticate against it.
/// </summary>
public sealed class HashPlaydexAppKeysMigration : IPlaydexMigration
{
    public string Id => "021_hash_playdex_app_keys";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        -- A key that several app integrations share never authenticated (capture needed exactly one match). Drop it
        -- rather than let whichever row stays sign in for another user; those users create a new key.
        update integrations
        set api_key = null
        where type = 'api'
          and api_key in (
              select api_key from integrations
              where type = 'api' and api_key is not null
              group by api_key
              having count(*) > 1);

        update integrations
        set api_key = 'sha256:' || encode(sha256(convert_to(api_key, 'UTF8')), 'hex')
        where type = 'api' and api_key is not null and api_key not like 'sha256:%';

        create unique index if not exists integrations_app_key_unique on integrations (api_key) where type = 'api';
        """;
}
