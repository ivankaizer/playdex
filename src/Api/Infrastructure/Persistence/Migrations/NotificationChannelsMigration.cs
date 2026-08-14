using Npgsql;

namespace Api.Infrastructure.Persistence.Migrations;

/// <summary>
/// Moves Telegram routing out of users and into generic, user-owned
/// notification channels.
/// </summary>
public sealed class NotificationChannelsMigration : IPlaydexMigration
{
    public string Id => "004_notification_channels";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            create table if not exists notification_channels (
                user_id bigint not null references users(id) on delete cascade,
                transport text not null,
                destination text not null,
                created_at timestamptz not null default now(),
                updated_at timestamptz not null default now(),
                constraint notification_channels_pkey primary key (user_id, transport),
                constraint notification_channels_transport_check
                    check (transport ~ '^[a-z][a-z0-9-]*$'),
                constraint notification_channels_destination_check
                    check (length(trim(destination)) > 0)
            );

            insert into notification_channels (user_id, transport, destination)
            select id, 'telegram', chat_id
            from users
            where chat_id is not null and length(trim(chat_id)) > 0
            on conflict (user_id, transport) do update set
                destination = excluded.destination,
                updated_at = now();

            drop index if exists users_chat_id_unique;
            alter table users drop column if exists chat_id;
            """,
            connection,
            transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
