using Npgsql;
using Xunit;

namespace Api.Tests;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class NotificationChannelsMigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task SchemaUsesChannelsAndRemovesLegacyUserChatId()
    {
        if (!postgres.IsConfigured) return;

        await using var command = postgres.DataSource!.CreateCommand(
            """
            select
                to_regclass('public.notification_channels') is not null,
                not exists (
                    select 1
                    from information_schema.columns
                    where table_schema = 'public'
                      and table_name = 'users'
                      and column_name = 'chat_id'),
                exists (
                    select 1
                    from information_schema.table_constraints constraint_row
                    join information_schema.key_column_usage key_row
                      on key_row.constraint_schema = constraint_row.constraint_schema
                     and key_row.constraint_name = constraint_row.constraint_name
                    where constraint_row.table_schema = 'public'
                      and constraint_row.table_name = 'notification_channels'
                      and constraint_row.constraint_type = 'PRIMARY KEY'
                    group by constraint_row.constraint_name
                    having array_agg(key_row.column_name order by key_row.ordinal_position)
                        = array['user_id', 'transport'])
            """);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(0));
        Assert.True(reader.GetBoolean(1));
        Assert.True(reader.GetBoolean(2));
    }
}
