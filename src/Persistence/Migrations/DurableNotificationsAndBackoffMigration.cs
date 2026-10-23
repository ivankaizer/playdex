using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// State that used to live in one API process: the poll failure backoff (each replica notified about the same
/// outage, and a restart forgot it) and the notifications waiting for delivery (lost at shutdown). Also records why
/// Playdex paused an integration whose credential the provider keeps rejecting. Additive: the previous release runs
/// against it unchanged.
/// </summary>
public sealed class DurableNotificationsAndBackoffMigration : IPlaydexMigration
{
    public string Id => "020_durable_notifications_and_backoff";
    public bool SupportsRollback => true;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        -- Set only when Playdex paused polling itself, so the user sees why; cleared when the user resumes.
        alter table integrations add column if not exists paused_reason text;
        alter table integrations add column if not exists paused_at timestamptz;

        -- No row: the integration is healthy and due. failures = 0: a provider asked to be left alone until
        -- next_poll_at. A deleted integration takes its row with it.
        create table if not exists integration_poll_backoff (
            integration_id bigint primary key references integrations(id) on delete cascade,
            failures integer not null default 0
                constraint integration_poll_backoff_failures_check check (failures >= 0),
            auth_failures integer not null default 0
                constraint integration_poll_backoff_auth_failures_check check (auth_failures >= 0),
            failing_since timestamptz,
            next_poll_at timestamptz not null,
            updated_at timestamptz not null
        );

        -- Written when the event happens and sent by whichever instance claims it first. Delivered rows are kept for
        -- a week so the same event (dedupe_key) is not queued twice.
        create table if not exists notification_outbox (
            id bigint generated always as identity primary key,
            dedupe_key text not null constraint notification_outbox_dedupe_key_unique unique,
            kind text not null,
            payload jsonb not null,
            attempts integer not null default 0,
            next_attempt_at timestamptz not null,
            last_error text,
            created_at timestamptz not null,
            delivered_at timestamptz,
            failed_at timestamptz
        );

        create index if not exists notification_outbox_pending_idx
            on notification_outbox (next_attempt_at) where delivered_at is null and failed_at is null;
        """;
}
