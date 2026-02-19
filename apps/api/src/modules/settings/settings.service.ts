import {
  settingsRepository,
  type RawUserSettingsRow,
} from "./settings.repository";

export type UserSettings = {
  ignoreShortSessionsMinutes: number;
};

const mapSettings = (row: RawUserSettingsRow | null | undefined): UserSettings => ({
  ignoreShortSessionsMinutes: row?.ignore_short_sessions_minutes ?? 0,
});

export const settingsService = {
  getUserSettings: async (userId: number): Promise<UserSettings> => {
    const row = await settingsRepository.loadUserSettings(userId);
    return mapSettings(row);
  },

  updateUserSettings: async (
    userId: number,
    ignoreShortSessionsMinutes: number
  ): Promise<UserSettings> => {
    const row = await settingsRepository.upsertUserSettings(
      userId,
      ignoreShortSessionsMinutes
    );
    return mapSettings(row);
  },
};
