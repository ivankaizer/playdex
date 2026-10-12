import type { Submission, TestNotificationResponse, UserSettingsResponse } from "../models";

export interface PreferencesState {
  ignoreShortSessionsMinutes: string;
  savedIgnoreShortSessionsMinutes: string;
  telegramChatId: string;
  savedTelegramChatId: string;
  /** The server sent a minimum above the stepper's range; the draft holds the clamped value until it is saved. */
  savedMinutesOutOfRange: boolean;
  isSettingsLoading: boolean;
  isSettingsSaving: boolean;
  settingsError: string;
  isTestNotificationSending: boolean;
  testNotificationMessage: string;
  testNotificationError: string;
}

export const settingsPath = "/settings";
export const testNotificationPath = "/settings/notifications/test";

/** One limit for the stepper, the presets, typed values and whatever the API sends back. */
export const MaxMinimumMinutes = 60;
export const minutesRangeMessage = `Enter a whole number of minutes from 0 to ${MaxMinimumMinutes}.`;
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
    savedMinutesOutOfRange: false,
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

/** True when either field differs from the last loaded or saved value (Discard has something to restore). */
export function hasDiscardableChanges(state: PreferencesState): boolean {
  return (
    state.ignoreShortSessionsMinutes !== state.savedIgnoreShortSessionsMinutes ||
    state.telegramChatId !== state.savedTelegramChatId
  );
}

/** True when saving would change what the server stores: an edited field, or a clamped out-of-range minimum. */
export function isPreferencesDirty(state: PreferencesState): boolean {
  return hasDiscardableChanges(state) || state.savedMinutesOutOfRange;
}

/** Save is offered only for a dirty, valid form while nothing else is in flight. */
export function canSavePreferences(state: PreferencesState): boolean {
  return (
    !isPreferencesBusy(state) &&
    isPreferencesDirty(state) &&
    parsedMinimumMinutes(state) !== null &&
    isTelegramChatIdDraftValid(state.telegramChatId)
  );
}

/** A blank chat ID (notifications off) or one the API accepts. */
export function isTelegramChatIdDraftValid(draft: string): boolean {
  const trimmed = draft.trim();
  return trimmed.length === 0 || isValidChatId(trimmed);
}

/** Clamps any number to the stepper's whole-minute range [0, MaxMinimumMinutes]. */
export function clampMinimumMinutes(value: number): number {
  return Number.isFinite(value) ? Math.max(0, Math.min(MaxMinimumMinutes, Math.round(value))) : 0;
}

/** Parsed minimum minutes for display and the stepper; null unless the draft is a whole number in [0, 60]. */
export function parsedMinimumMinutes(state: PreferencesState): number | null {
  const draft = state.ignoreShortSessionsMinutes.trim();
  if (draft.length === 0) return 0;
  if (!/^\d+$/.test(draft)) return null;
  const parsed = Number(draft);
  return parsed > MaxMinimumMinutes ? null : parsed;
}

/** Steps the minimum minutes by delta, clamped to [0, 60]; an unparsable draft steps from 0. */
export function stepIgnoreShortSessionsMinutes(state: PreferencesState, delta: number): PreferencesState {
  const current = parsedMinimumMinutes(state) ?? 0;
  return setIgnoreShortSessionsMinutes(state, String(clampMinimumMinutes(current + delta)));
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
    state: { ...state, settingsError: minutesRangeMessage },
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

  if (!Number.isFinite(parsed) || parsed < 0 || Math.trunc(parsed) !== parsed || parsed > MaxMinimumMinutes) {
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
    ? { ...state, testNotificationMessage: `Test message sent to chat ${state.savedTelegramChatId}.` }
    : { ...state, testNotificationError: "The test message was not sent." };
}

export function failTestNotification(state: PreferencesState, error: string): PreferencesState {
  return { ...state, testNotificationError: error };
}

export function endTestNotification(state: PreferencesState): PreferencesState {
  return { ...state, isTestNotificationSending: false };
}

// A stored value outside the stepper's range is shown clamped and marks the form dirty, so the next save writes it back.
function applySettingsResponse(state: PreferencesState, response: UserSettingsResponse): PreferencesState {
  const telegramChatId = response.telegramChatId ?? "";
  const minutes = clampMinimumMinutes(response.ignoreShortSessionsMinutes);
  return {
    ...state,
    ignoreShortSessionsMinutes: String(minutes),
    savedIgnoreShortSessionsMinutes: String(minutes),
    savedMinutesOutOfRange: minutes !== response.ignoreShortSessionsMinutes,
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
