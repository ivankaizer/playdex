import {
  loadUserSettings,
  upsertUserSettings,
  type UserSettingsRow,
} from "../../shared/db/queries";

export type RawUserSettingsRow = UserSettingsRow;

export const settingsRepository = {
  loadUserSettings: (userId: number) => loadUserSettings(userId),
  upsertUserSettings: (userId: number, ignoreShortSessionsMinutes: number) =>
    upsertUserSettings(userId, ignoreShortSessionsMinutes),
};
