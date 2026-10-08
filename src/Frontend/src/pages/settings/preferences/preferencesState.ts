import type { Submission, TestNotificationResponse, UserSettingsResponse } from "../models";

export interface PreferencesState {
  ignoreShortSessionsMinutes: string;
  savedIgnoreShortSessionsMinutes: string;
  telegramChatId: string;
  savedTelegramChatId: string;
  isSettingsLoading: boolean;
  isSettingsSaving: boolean;
  settingsError: string;
  isTestNotificationSending: boolean;
  testNotificationMessage: string;
  testNotificationError: string;
}

export const settingsPath = "/settings";
export const testNotificationPath = "/settings/notifications/test";

const MaxInt32 = 2_147_483_647;
const MinInt64 = -(2n ** 63n);
const MaxInt64 = 2n ** 63n - 1n;
// double.TryParse(NumberStyles.Float, InvariantCulture) accepts ASCII-whitespace-padded, signed decimals with exponents.
const FloatPattern = /^[\t-\r ]*[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?[\t-\r ]*$/;

export function createPreferencesState(): PreferencesState {
  return {
    ignoreShortSessionsMinutes: "0",
    savedIgnoreShortSessionsMinutes: "0",
    telegramChatId: "",
    savedTelegramChatId: "",
    isSettingsLoading: false,
    isSettingsSaving: false,
    settingsError: "",
    isTestNotificationSending: false,
    testNotificationMessage: "",
    testNotificationError: "",
  };
}

export function isPreferencesBusy(state: PreferencesState): boolean {
  return state.isSettingsLoading || state.isSettingsSaving || state.isTestNotificationSending;
}

export function canSendTestNotification(state: PreferencesState): boolean {
  return (
    !isPreferencesBusy(state) &&
    state.savedTelegramChatId.length > 0 &&
    state.telegramChatId.trim() === state.savedTelegramChatId
  );
}

/** True when either field differs from the last loaded or saved value. */
export function isPreferencesDirty(state: PreferencesState): boolean {
  return (
    state.ignoreShortSessionsMinutes !== state.savedIgnoreShortSessionsMinutes ||
    state.telegramChatId !== state.savedTelegramChatId
  );
}

/** Parsed minimum minutes for display and the stepper; null when the draft is not a whole non-negative number. */
export function parsedMinimumMinutes(state: PreferencesState): number | null {
  const draft = state.ignoreShortSessionsMinutes.trim();
  if (draft.length === 0) return 0;
  if (!/^\d+$/.test(draft)) return null;
  const parsed = Number(draft);
  return parsed > MaxInt32 ? null : parsed;
}

/** Steps the minimum minutes by delta, clamped to [0, MaxInt32]; an unparsable draft steps from 0. */
export function stepIgnoreShortSessionsMinutes(state: PreferencesState, delta: number): PreferencesState {
  const current = parsedMinimumMinutes(state) ?? 0;
  const next = Math.max(0, Math.min(MaxInt32, current + delta));
  return setIgnoreShortSessionsMinutes(state, String(next));
}

/** Restores both fields to the last loaded or saved values. */
export function discardSettingsChanges(state: PreferencesState): PreferencesState {
  return {
    ...state,
    ignoreShortSessionsMinutes: state.savedIgnoreShortSessionsMinutes,
    telegramChatId: state.savedTelegramChatId,
    settingsError: "",
    testNotificationMessage: "",
    testNotificationError: "",
  };
}

export function beginSettingsLoad(state: PreferencesState): PreferencesState {
  return { ...state, isSettingsLoading: true, settingsError: "" };
}

export function completeSettingsLoad(state: PreferencesState, response: UserSettingsResponse): PreferencesState {
  return { ...applySettingsResponse(state, response), isSettingsLoading: false };
}

export function failSettingsLoad(state: PreferencesState, error: string): PreferencesState {
  return { ...state, isSettingsLoading: false, settingsError: error };
}

export function setIgnoreShortSessionsMinutes(state: PreferencesState, value: string): PreferencesState {
  return { ...state, ignoreShortSessionsMinutes: value };
}

export function setTelegramChatId(state: PreferencesState, value: string): PreferencesState {
  return { ...state, telegramChatId: value, testNotificationMessage: "", testNotificationError: "" };
}

export function buildSettingsSubmission(state: PreferencesState): {
  state: PreferencesState;
  submission: Submission | null;
} {
  const invalidMinutes = {
    state: { ...state, settingsError: "Enter a non-negative number of minutes." },
    submission: null,
  };

  const draft = state.ignoreShortSessionsMinutes;
  let parsed: number;
  if (draft.trim().length === 0) {
    parsed = 0;
  } else if (!FloatPattern.test(draft)) {
    return invalidMinutes;
  } else {
    parsed = Number(draft.trim());
  }

  if (!Number.isFinite(parsed) || parsed < 0 || Math.trunc(parsed) !== parsed || parsed > MaxInt32) {
    return invalidMinutes;
  }

  const telegramChatId = state.telegramChatId.trim();
  if (telegramChatId.length > 0 && !isValidChatId(telegramChatId)) {
    return { state: { ...state, settingsError: "Enter a valid Telegram chat ID." }, submission: null };
  }

  return {
    state,
    submission: {
      path: settingsPath,
      method: "PUT",
      body: {
        ignoreShortSessionsMinutes: parsed === 0 ? 0 : parsed,
        telegramChatId: telegramChatId.length === 0 ? null : telegramChatId,
      },
    },
  };
}

export function beginSettingsSave(state: PreferencesState): PreferencesState {
  return {
    ...state,
    isSettingsSaving: true,
    settingsError: "",
    testNotificationMessage: "",
    testNotificationError: "",
  };
}

export function completeSettingsSave(state: PreferencesState, response: UserSettingsResponse): PreferencesState {
  return applySettingsResponse(state, response);
}

export function failSettingsSave(state: PreferencesState, error: string): PreferencesState {
  return { ...state, settingsError: error };
}

export function endSettingsSave(state: PreferencesState): PreferencesState {
  return { ...state, isSettingsSaving: false };
}

export function beginTestNotification(state: PreferencesState): PreferencesState {
  return { ...state, isTestNotificationSending: true, testNotificationMessage: "", testNotificationError: "" };
}

export function completeTestNotification(
  state: PreferencesState,
  response: TestNotificationResponse,
): PreferencesState {
  return response.sent
    ? { ...state, testNotificationMessage: "Test message sent." }
    : { ...state, testNotificationError: "The test message was not sent." };
}

export function failTestNotification(state: PreferencesState, error: string): PreferencesState {
  return { ...state, testNotificationError: error };
}

export function endTestNotification(state: PreferencesState): PreferencesState {
  return { ...state, isTestNotificationSending: false };
}

function applySettingsResponse(state: PreferencesState, response: UserSettingsResponse): PreferencesState {
  const telegramChatId = response.telegramChatId ?? "";
  return {
    ...state,
    ignoreShortSessionsMinutes: String(response.ignoreShortSessionsMinutes),
    savedIgnoreShortSessionsMinutes: String(response.ignoreShortSessionsMinutes),
    telegramChatId,
    savedTelegramChatId: telegramChatId,
  };
}

// long.TryParse(NumberStyles.AllowLeadingSign, InvariantCulture) and non-zero.
function isValidChatId(value: string): boolean {
  if (!/^[+-]?\d+$/.test(value)) return false;
  const parsed = BigInt(value);
  return parsed >= MinInt64 && parsed <= MaxInt64 && parsed !== 0n;
}
