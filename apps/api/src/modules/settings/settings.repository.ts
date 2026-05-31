import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";

export type UserSettingsRow = {
  user_id: number;
  ignore_short_sessions_minutes: number;
};

export type RawUserSettingsRow = UserSettingsRow;

const normalizeUserSettingsRow = (row: {
  user_id: string;
  ignore_short_sessions_minutes: number;
}): UserSettingsRow => ({
  ...row,
  user_id: toDomainId(row.user_id, "user_settings.user_id"),
});

const loadUserSettings = async (userId: number, db: DbExecutor = getDb()) => {
  const row = await db
    .selectFrom("user_settings")
    .select(["user_id", "ignore_short_sessions_minutes"])
    .where("user_id", "=", String(userId))
    .executeTakeFirst();

  return row ? normalizeUserSettingsRow(row) : null;
};

const upsertUserSettings = async (
  userId: number,
  ignoreShortSessionsMinutes: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .insertInto("user_settings")
    .values({
      user_id: userId,
      ignore_short_sessions_minutes: ignoreShortSessionsMinutes,
    })
    .onConflict((oc) =>
      oc.column("user_id").doUpdateSet((eb) => ({
        ignore_short_sessions_minutes: eb.ref(
          "excluded.ignore_short_sessions_minutes"
        ),
        updated_at: eb.fn<Date>("now"),
      }))
    )
    .returning(["user_id", "ignore_short_sessions_minutes"])
    .executeTakeFirst();

  return row ? normalizeUserSettingsRow(row) : null;
};

export const settingsRepository = {
  loadUserSettings: (userId: number) => loadUserSettings(userId),
  upsertUserSettings: (userId: number, ignoreShortSessionsMinutes: number) =>
    upsertUserSettings(userId, ignoreShortSessionsMinutes),
};
