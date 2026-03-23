import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

type Sql = ReturnType<typeof postgres>;

export type UserSettingsRow = {
  user_id: number;
  ignore_short_sessions_minutes: number;
};

export type RawUserSettingsRow = UserSettingsRow;

const loadUserSettings = async (userId: number, sql: Sql = getSql()) => {
  const rows = await sql<UserSettingsRow[]>`
    select user_id, ignore_short_sessions_minutes
    from user_settings
    where user_id = ${userId}
    limit 1
  `;
  return rows[0] ?? null;
};

const upsertUserSettings = async (
  userId: number,
  ignoreShortSessionsMinutes: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<UserSettingsRow[]>`
    insert into user_settings (user_id, ignore_short_sessions_minutes)
    values (${userId}, ${ignoreShortSessionsMinutes})
    on conflict (user_id)
    do update
      set ignore_short_sessions_minutes = excluded.ignore_short_sessions_minutes,
          updated_at = now()
    returning user_id, ignore_short_sessions_minutes
  `;
  return rows[0] ?? null;
};

export const settingsRepository = {
  loadUserSettings: (userId: number) => loadUserSettings(userId),
  upsertUserSettings: (userId: number, ignoreShortSessionsMinutes: number) =>
    upsertUserSettings(userId, ignoreShortSessionsMinutes),
};
