import { describe, expect, it } from "vitest";
import * as p from "./preferencesState";

function withMinutes(draft: string) {
  return p.setIgnoreShortSessionsMinutes(p.createPreferencesState(), draft);
}

describe("preferencesState", () => {
  it("retains values while loading and fails independently", () => {
    let state = p.completeSettingsLoad(p.createPreferencesState(), {
      ignoreShortSessionsMinutes: 30,
      telegramChatId: "123456789",
    });
    state = p.beginSettingsLoad(state);
    expect(state.isSettingsLoading).toBe(true);
    expect(state.ignoreShortSessionsMinutes).toBe("30");
    expect(state.telegramChatId).toBe("123456789");

    state = p.failSettingsLoad(state, "Settings error.");
    expect(state.settingsError).toBe("Settings error.");
    expect(state.isSettingsLoading).toBe(false);
    expect(state.ignoreShortSessionsMinutes).toBe("30");
  });

  it.each([
    ["", 0],
    ["   ", 0],
    ["0", 0],
    ["1.0", 1],
    ["1e1", 10],
    ["60", 60],
    [" 15 ", 15],
  ])("accepts integer minutes %j", (draft, expected) => {
    const { submission } = p.buildSettingsSubmission(withMinutes(draft));
    expect(submission).not.toBeNull();
    expect(submission!.path).toBe("/settings");
    expect(submission!.method).toBe("PUT");
    expect(Object.keys(submission!.body)).toEqual(["ignoreShortSessionsMinutes", "telegramChatId"]);
    expect(submission!.body.ignoreShortSessionsMinutes).toBe(expected);
    expect(submission!.body.telegramChatId).toBeNull();
  });

  it.each(["-1", "1.5", "nope", "Infinity", "NaN", "0x10", "1e400", "2147483648", "1,000", "61", "1e2"])(
    "rejects non-integer, negative or out-of-range minutes %j",
    (draft) => {
      const result = p.buildSettingsSubmission(withMinutes(draft));
      expect(result.submission).toBeNull();
      expect(result.state.settingsError).toBe("Enter a whole number of minutes from 0 to 60.");
    },
  );

  it.each([
    ["123456789", "123456789"],
    ["  -100123456789  ", "-100123456789"],
  ])("accepts Telegram chat id %j", (draft, expected) => {
    const state = p.setTelegramChatId(p.createPreferencesState(), draft);
    expect(p.buildSettingsSubmission(state).submission!.body.telegramChatId).toBe(expected);
  });

  it.each(["0", "@username", "12.5", "-0", "9223372036854775808"])("rejects Telegram chat id %j", (draft) => {
    const state = p.setTelegramChatId(p.createPreferencesState(), draft);
    const result = p.buildSettingsSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.settingsError).toBe("Enter a valid Telegram chat ID.");
  });

  it("uses the save response without a success state", () => {
    let state = p.beginSettingsSave(withMinutes("15"));
    expect(state.isSettingsSaving).toBe(true);
    expect(state.settingsError).toBe("");
    state = p.endSettingsSave(p.completeSettingsSave(state, { ignoreShortSessionsMinutes: 20, telegramChatId: "987654321" }));
    expect(state.isSettingsSaving).toBe(false);
    expect(state.ignoreShortSessionsMinutes).toBe("20");
    expect(state.telegramChatId).toBe("987654321");

    state = p.endSettingsSave(p.failSettingsSave(p.beginSettingsSave(state), "Save failed."));
    expect(state.settingsError).toBe("Save failed.");
    expect(state.ignoreShortSessionsMinutes).toBe("20");
  });

  it("requires an unchanged saved chat id to send a test notification and reports the outcome", () => {
    let state = p.createPreferencesState();
    expect(p.canSendTestNotification(state)).toBe(false);
    state = p.completeSettingsLoad(state, { ignoreShortSessionsMinutes: 0, telegramChatId: "123456789" });
    expect(p.canSendTestNotification(state)).toBe(true);
    state = p.setTelegramChatId(state, "987654321");
    expect(p.canSendTestNotification(state)).toBe(false);
    state = p.completeSettingsSave(state, { ignoreShortSessionsMinutes: 0, telegramChatId: "987654321" });
    expect(p.canSendTestNotification(state)).toBe(true);

    state = p.beginTestNotification(state);
    expect(state.isTestNotificationSending).toBe(true);
    expect(p.canSendTestNotification(state)).toBe(false);
    state = p.endTestNotification(p.completeTestNotification(state, { sent: true }));
    expect(p.canSendTestNotification(state)).toBe(true);
    expect(state.testNotificationMessage).toBe("Test message sent to chat 987654321.");
    expect(state.testNotificationError).toBe("");

    state = p.endTestNotification(p.failTestNotification(p.beginTestNotification(state), "Apprise unavailable."));
    expect(state.testNotificationError).toBe("Apprise unavailable.");
    expect(state.testNotificationMessage).toBe("");

    state = p.completeTestNotification(p.beginTestNotification(state), { sent: false });
    expect(state.testNotificationError).toBe("The test message was not sent.");
  });

  it("tracks unsaved changes and discards back to the loaded values", () => {
    const loaded = p.completeSettingsLoad(p.createPreferencesState(), {
      ignoreShortSessionsMinutes: 5,
      telegramChatId: "42",
    });
    expect(p.isPreferencesDirty(loaded)).toBe(false);

    const stepped = p.stepIgnoreShortSessionsMinutes(loaded, 1);
    expect(stepped.ignoreShortSessionsMinutes).toBe("6");
    expect(p.isPreferencesDirty(stepped)).toBe(true);

    const edited = p.setTelegramChatId(stepped, "43");
    const discarded = p.discardSettingsChanges(edited);
    expect([discarded.ignoreShortSessionsMinutes, discarded.telegramChatId]).toEqual(["5", "42"]);
    expect(p.isPreferencesDirty(discarded)).toBe(false);
  });

  it("steps minimum minutes from 0 for unparsable drafts and never below 0", () => {
    const state = p.setIgnoreShortSessionsMinutes(p.createPreferencesState(), "abc");
    expect(p.parsedMinimumMinutes(state)).toBeNull();
    expect(p.stepIgnoreShortSessionsMinutes(state, 1).ignoreShortSessionsMinutes).toBe("1");
    expect(p.stepIgnoreShortSessionsMinutes(p.createPreferencesState(), -1).ignoreShortSessionsMinutes).toBe("0");
    expect(p.parsedMinimumMinutes(p.setIgnoreShortSessionsMinutes(state, " "))).toBe(0);
  });

  it("caps stepping at 60 and treats typed values above 60 as invalid", () => {
    const at60 = withMinutes("60");
    expect(p.parsedMinimumMinutes(at60)).toBe(60);
    expect(p.stepIgnoreShortSessionsMinutes(at60, 1).ignoreShortSessionsMinutes).toBe("60");
    expect(p.parsedMinimumMinutes(withMinutes("61"))).toBeNull();
    expect(p.stepIgnoreShortSessionsMinutes(withMinutes("500"), 1).ignoreShortSessionsMinutes).toBe("1");
  });

  it("shows a stored minimum above 60 clamped and keeps the form dirty until it is saved", () => {
    let state = p.completeSettingsLoad(p.createPreferencesState(), { ignoreShortSessionsMinutes: 120, telegramChatId: null });
    expect(state.ignoreShortSessionsMinutes).toBe("60");
    expect(p.hasDiscardableChanges(state)).toBe(false);
    expect(p.isPreferencesDirty(state)).toBe(true);
    expect(p.canSavePreferences(state)).toBe(true);

    // Discard restores the clamped value; the write-back is still pending.
    state = p.discardSettingsChanges(p.stepIgnoreShortSessionsMinutes(state, -5));
    expect(state.ignoreShortSessionsMinutes).toBe("60");
    expect(p.isPreferencesDirty(state)).toBe(true);
    expect(p.buildSettingsSubmission(state).submission!.body.ignoreShortSessionsMinutes).toBe(60);

    state = p.completeSettingsSave(state, { ignoreShortSessionsMinutes: 60, telegramChatId: null });
    expect(p.isPreferencesDirty(state)).toBe(false);
  });

  it("only allows saving a dirty form with valid minutes and chat ID", () => {
    const loaded = p.completeSettingsLoad(p.createPreferencesState(), { ignoreShortSessionsMinutes: 5, telegramChatId: null });
    expect(p.canSavePreferences(loaded)).toBe(false);
    expect(p.canSavePreferences(p.setIgnoreShortSessionsMinutes(loaded, "6"))).toBe(true);
    expect(p.canSavePreferences(p.setIgnoreShortSessionsMinutes(loaded, "90"))).toBe(false);
    expect(p.canSavePreferences(p.setTelegramChatId(loaded, "@channel"))).toBe(false);
    expect(p.canSavePreferences(p.setTelegramChatId(loaded, " -1001234567890 "))).toBe(true);
    expect(p.isTelegramChatIdDraftValid("  ")).toBe(true);
  });
});
