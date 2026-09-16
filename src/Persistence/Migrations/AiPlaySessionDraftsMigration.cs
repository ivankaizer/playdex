using Npgsql;

namespace Playdex.Persistence.Migrations;

public sealed class AiPlaySessionDraftsMigration : IPlaydexMigration
{
    public string Id => "013_ai_play_session_drafts";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            do $$
            begin
                if not exists (
                    select 1
                    from pg_constraint
                    where conrelid = 'users'::regclass
                      and contype = 'p'
                ) then
                    alter table users add constraint users_pkey primary key (id);
                end if;
            end
            $$;

            create table if not exists session_draft_batches (
                id uuid primary key,
                user_id bigint not null references users(id) on delete cascade,
                request_id uuid not null,
                created_at timestamptz not null,
                timezone text not null,
                reference_date date not null,
                constraint session_draft_batches_request_unique unique (user_id, request_id)
            );
            create index if not exists session_draft_batches_owner_created
                on session_draft_batches(user_id, created_at desc);
            create table if not exists session_draft_rows (
                id uuid primary key,
                batch_id uuid not null references session_draft_batches(id) on delete cascade,
                position integer not null check (position >= 0),
                title text not null,
                date text,
                duration_minutes integer,
                platform_key text not null default 'unknown',
                game_id text,
                igdb_id text,
                selection_json jsonb,
                warnings text[] not null default '{}',
                revision integer not null default 1,
                status text not null default 'pending',
                accepted_session_id bigint,
                constraint session_draft_rows_position_unique unique (batch_id, position),
                constraint session_draft_rows_revision_check check (revision >= 1),
                constraint session_draft_rows_status_check check (status in ('pending', 'accepted', 'deleted')),
                constraint session_draft_rows_accepted_link_check check (
                    (status = 'accepted' and accepted_session_id is not null) or
                    (status <> 'accepted' and accepted_session_id is null))
            );
            create unique index if not exists session_draft_rows_accepted_session_unique
                on session_draft_rows(accepted_session_id) where accepted_session_id is not null;
            create table if not exists draft_extraction_attempts (
                user_id bigint primary key references users(id) on delete cascade,
                window_started_at timestamptz not null,
                attempt_count integer not null check (attempt_count > 0)
            );
            """, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
