import { cx } from "@/lib/cx";
import { SectionHeading } from "../shared/SectionHeading";
import {
  canSendTestNotification,
  isPreferencesBusy,
  isPreferencesDirty,
  parsedMinimumMinutes,
} from "./preferencesState";
import type { usePreferences } from "./usePreferences";

interface PreferencesSectionProps {
  preferences: ReturnType<typeof usePreferences>;
}

const presets = [0, 2, 5, 10] as const;

const stepButtonClass =
  "grid h-10 w-10 place-items-center rounded-full bg-tile text-lg font-semibold text-ink transition-colors hover:bg-track disabled:cursor-not-allowed disabled:opacity-50";

export function PreferencesSection({ preferences }: PreferencesSectionProps) {
  const { state } = preferences;
  const busy = isPreferencesBusy(state);
  const dirty = isPreferencesDirty(state);
  const minutes = parsedMinimumMinutes(state);
  const telegramOn = state.savedTelegramChatId.length > 0;

  const minutesNote =
    minutes === null
      ? "Enter a whole number of minutes."
      : minutes === 0
        ? "Every session is counted, however short."
        : `Sessions under ${minutes} min are ignored.`;

  const saveNote = state.settingsError ? (
    <span className="text-danger">{state.settingsError}</span>
  ) : dirty ? (
    <span className="text-warn">You have unsaved changes</span>
  ) : state.isSettingsLoading ? (
    <span className="text-muted">Loading your settings…</span>
  ) : (
    <span className="text-muted">All changes saved</span>
  );

  return (
    <section data-settings-section="preferences" className="pdx-card flex flex-col gap-7">
      <SectionHeading
        kicker="Your preferences"
        title="Personal settings"
        description="Tune how Playdex records short sessions and reaches you."
      />

      <div className="grid gap-4 [grid-template-columns:repeat(auto-fit,minmax(min(340px,100%),1fr))]">
        <div data-settings-group="sessions" className="pdx-tile flex flex-col gap-5 !p-7">
          <div>
            <h3 className="text-[17px] font-semibold">Session filtering</h3>
            <p className="mt-1.5 text-sm leading-normal text-muted [text-wrap:pretty]">
              Sessions shorter than this are marked ignored and left out of stats.
            </p>
          </div>
          <div className="flex flex-wrap items-center gap-4">
            <div className="flex items-center gap-1 rounded-full border border-line bg-surface p-1 transition-[border-color,box-shadow] focus-within:border-bar focus-within:shadow-[0_0_0_3px_rgb(var(--bar)/0.18)]">
              <button
                type="button"
                aria-label="Decrease minimum minutes"
                onClick={() => preferences.stepIgnoreShortSessionsMinutes(-1)}
                disabled={busy || minutes === 0}
                className={stepButtonClass}
              >
                −
              </button>
              <label className="flex min-w-[92px] items-baseline justify-center gap-1 px-1 text-lg font-semibold">
                <span className="sr-only">Minimum minutes</span>
                <input
                  type="number"
                  min="0"
                  step="1"
                  inputMode="numeric"
                  value={state.ignoreShortSessionsMinutes}
                  onChange={(event) => preferences.setIgnoreShortSessionsMinutes(event.target.value)}
                  disabled={busy}
                  style={{ width: `${Math.min(10, Math.max(1, state.ignoreShortSessionsMinutes.length)) + 0.6}ch` }}
                  className="pdx-num appearance-none bg-transparent text-right text-lg font-semibold text-ink outline-none [-moz-appearance:textfield] disabled:opacity-60 [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
                />
                <span aria-hidden="true" className="text-[15px] text-muted">
                  min
                </span>
              </label>
              <button
                type="button"
                aria-label="Increase minimum minutes"
                onClick={() => preferences.stepIgnoreShortSessionsMinutes(1)}
                disabled={busy}
                className={stepButtonClass}
              >
                +
              </button>
            </div>
            <div className="flex flex-wrap gap-1.5" role="group" aria-label="Minimum minutes presets">
              {presets.map((value) => (
                <button
                  key={value}
                  type="button"
                  aria-pressed={minutes === value}
                  onClick={() => preferences.setIgnoreShortSessionsMinutes(String(value))}
                  disabled={busy}
                  className="pdx-chip !h-[34px] !px-3.5 font-semibold"
                >
                  {value === 0 ? "Off" : `${value}m`}
                </button>
              ))}
            </div>
          </div>
          <p className={cx("text-[13px]", minutes === null ? "text-danger" : "text-ink-2")}>{minutesNote}</p>
        </div>

        <div data-settings-group="telegram" className="pdx-tile flex flex-col gap-5 !p-7">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <h3 className="text-[17px] font-semibold">Telegram notifications</h3>
              <p className="mt-1.5 text-sm leading-normal text-muted [text-wrap:pretty]">
                Message the Playdex bot once, then save your chat ID. Leave it blank to turn notifications off.
              </p>
            </div>
            <span
              data-telegram-status={telegramOn ? "on" : "off"}
              className={cx(
                "shrink-0 rounded-full px-3 py-[5px] text-[10px] font-semibold tracking-[0.2em]",
                telegramOn ? "bg-mint text-accent" : "bg-divider text-muted",
              )}
            >
              {telegramOn ? "ON" : "OFF"}
            </span>
          </div>
          <div className="flex flex-wrap gap-2.5">
            <label className="min-w-0 flex-[1_1_200px]">
              <span className="sr-only">Chat ID</span>
              <input
                type="text"
                inputMode="numeric"
                placeholder="Chat ID"
                value={state.telegramChatId}
                onChange={(event) => preferences.setTelegramChatId(event.target.value)}
                disabled={busy}
                className="pdx-input pdx-num !h-12 !px-5 !text-[15px]"
              />
            </label>
            <button
              type="button"
              onClick={() => void preferences.sendTestNotification()}
              disabled={!canSendTestNotification(state)}
              className="pdx-btn-outline !h-12 text-ink"
            >
              {state.isTestNotificationSending ? "Sending..." : "Send test"}
            </button>
          </div>
          <p className="text-[13px]" aria-live="polite">
            {state.testNotificationMessage ? (
              <span className="text-accent">
                <span aria-hidden="true">✓ </span>
                {state.testNotificationMessage}
              </span>
            ) : state.testNotificationError ? (
              <span className="text-danger">{state.testNotificationError}</span>
            ) : state.savedTelegramChatId.length === 0 || state.telegramChatId.trim() !== state.savedTelegramChatId ? (
              <span className="text-muted">Save a chat ID to send a test message.</span>
            ) : (
              <span className="text-muted">Send a test to check the bot can reach you.</span>
            )}
          </p>
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-end gap-3 border-t border-divider pt-5">
        <p className="mr-auto text-[13px]" aria-live="polite">
          {saveNote}
        </p>
        {dirty ? (
          <button
            type="button"
            onClick={preferences.discardChanges}
            disabled={busy}
            className="pdx-btn-outline !h-11 text-ink"
          >
            Discard
          </button>
        ) : null}
        <button
          type="button"
          onClick={() => void preferences.saveSettings()}
          disabled={busy || !dirty}
          className="pdx-btn-primary !h-11 !px-6"
        >
          {state.isSettingsSaving ? "Saving..." : state.isSettingsLoading ? "Loading..." : "Save changes"}
        </button>
      </div>
    </section>
  );
}
