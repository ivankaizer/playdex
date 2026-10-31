using Npgsql;
using Playdex.Persistence.Migrations;
using Xunit;

namespace Api.Tests;

/// <summary>Runs <see cref="OAuthMigration"/> again inside a transaction that is rolled back, against the migrated database.</summary>
[Collection(PostgresCollectionDefinition.Name)]
public sealed class OAuthMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task TablesCascadeFromTheUserAndTheMigrationRunsAgain()
    {
        if (!postgres.IsConfigured)
        {
            return;
        }

        await using var connection = await postgres.DataSource!.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await new OAuthMigration().ApplyAsync(connection, transaction, CancellationToken.None);
        await new OAuthMigration().ApplyAsync(connection, transaction, CancellationToken.None);

        await using var command = new NpgsqlCommand("""
            with u as (insert into users (email, created_at, updated_at) values ('oauth-migration-' || gen_random_uuid() || '@test.local', now(), now()) returning id),
            g as (insert into oauth_grants (id, user_id, client_id, client_name, scope, resource, token_version)
                  select gen_random_uuid(), id, 'https://a.example/c.json', 'A', 'drafts', 'https://x/api/mcp', 0 from u returning id, user_id),
            c as (insert into oauth_authorization_codes (code_hash, user_id, client_id, client_name, redirect_uri, code_challenge, scope, resource, expires_at, grant_id)
                  select '\x01'::bytea, user_id, 'https://a.example/c.json', 'A', 'https://a.example/cb', 'x', 'drafts', 'https://x/api/mcp', now(), id from g),
            r as (insert into oauth_refresh_tokens (grant_id, token_hash, expires_at) select id, '\x02'::bytea, now() from g)
            select id from u;
            """, connection, transaction);
        var userId = (long)(await command.ExecuteScalarAsync())!;

        await using var delete = new NpgsqlCommand($"""
            delete from users where id = {userId};
            select (select count(*) from oauth_grants where user_id = {userId})
                 + (select count(*) from oauth_authorization_codes where user_id = {userId})
                 + (select count(*) from oauth_refresh_tokens where token_hash = '\x02'::bytea);
            """, connection, transaction);
        Assert.Equal(0L, await delete.ExecuteScalarAsync());
        await transaction.RollbackAsync();
    }
}
