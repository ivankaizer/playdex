using Npgsql;

namespace Playdex.Persistence.Migrations;

/// <summary>
/// At most one open session per polling integration, per game of an API integration and per user's manual timer.
/// Open duplicates left by races before the session locks are ended first: in each group the session started last
/// stays open and every other one ends at its last update (a manual timer: when the newest one started), never after
/// the newest one started.
/// </summary>
public sealed class OpenSessionGuardMigration : IPlaydexMigration
{
    public string Id => "017_open_session_guard";
    public bool SupportsRollback => false;

    public async Task ApplyAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private const string Sql = """
        with open_sessions as (
            select ps.id,
                   first_value(ps.started_at) over newest_first as newest_started_at,
                   row_number() over newest_first as position
            from play_sessions ps
            where ps.ended_at is null
            window newest_first as (
                partition by ps.capture_method,
                    case when ps.capture_method = 'manual' then ps.user_id else ps.integration_id end,
                    case when ps.capture_method = 'api' then ps.game_id end
                order by ps.started_at desc, ps.id desc)
        )
        update play_sessions ps
        set ended_at = greatest(
                ps.started_at,
                case when ps.capture_method = 'manual' then o.newest_started_at
                     else least(ps.updated_at, o.newest_started_at) end),
            updated_at = now()
        from open_sessions o
        where o.id = ps.id
          and o.position > 1;

        create unique index play_sessions_open_polling_key on play_sessions (integration_id)
            where ended_at is null and capture_method = 'polling';
        create unique index play_sessions_open_api_key on play_sessions (integration_id, game_id)
            where ended_at is null and capture_method = 'api';
        create unique index play_sessions_open_manual_key on play_sessions (user_id)
            where ended_at is null and capture_method = 'manual';
        """;
}
