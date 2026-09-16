using Npgsql;

namespace Playdex.Persistence.Migrations;

public sealed class AiGenerationHistoryMigration : IPlaydexMigration
{
    public string Id => "014_ai_generation_history";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            create table if not exists ai_generations (
                id uuid primary key,
                user_id bigint not null references users(id) on delete cascade,
                request_id uuid not null,
                created_at timestamptz not null,
                completed_at timestamptz,
                provider text not null,
                model text not null,
                timezone text not null,
                reference_date date not null,
                input_text text,
                image bytea,
                image_content_type text,
                status text not null default 'running' check (status in ('running','success','failure','timeout','cancelled')),
                error text,
                result_json jsonb,
                responses_json jsonb not null default '[]'
            );
            create index if not exists ai_generations_created_at on ai_generations(created_at desc);
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
