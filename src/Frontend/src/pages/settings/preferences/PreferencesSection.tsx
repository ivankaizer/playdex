import { canSendTestNotification, isPreferencesBusy } from "./preferencesState";
import type { usePreferences } from "./usePreferences";

interface PreferencesSectionProps {
  preferences: ReturnType<typeof usePreferences>;
}

export function PreferencesSection({ preferences }: PreferencesSectionProps) {
  const { state } = preferences;
  const busy = isPreferencesBusy(state);

  return (
    <section
      data-settings-section="preferences"
      className="rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6"
    >
      <div>
        <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
          Your preferences
        </p>
        <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">Personal settings</h2>
        <p className="mt-2 text-xs text-slate-500 dark:text-slate-400">
          Tune how Playdex records short sessions and reaches you.
        </p>
      </div>

      <div className="mt-6 grid gap-5 lg:grid-cols-2">
        <div
          data-settings-group="sessions"
          className="rounded-2xl border border-slate-200 bg-slate-100/60 p-4 dark:border-slate-800 dark:bg-slate-950/50 sm:p-5"
        >
          <h3 className="text-sm font-semibold text-slate-900 dark:text-white">Session filtering</h3>
          <p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">
            Sessions shorter than this duration are marked ignored and excluded from stats. Set to 0 to disable.
          </p>
          <label className="mt-5 block max-w-48 text-xs font-medium text-slate-600 dark:text-slate-300">
            <span className="block">Minimum minutes</span>
            <input
              type="number"
              min="0"
              step="1"
              inputMode="numeric"
              value={state.ignoreShortSessionsMinutes}
              onChange={(event) => preferences.setIgnoreShortSessionsMinutes(event.target.value)}
              disabled={busy}
              className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
            />
          </label>
        </div>

        <div
          data-settings-group="telegram"
          className="rounded-2xl border border-slate-200 bg-slate-100/60 p-4 dark:border-slate-800 dark:bg-slate-950/50 sm:p-5"
        >
          <h3 className="text-sm font-semibold text-slate-900 dark:text-white">Telegram notifications</h3>
          <p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">
            Message the Playdex bot once, then save your chat ID. Leave it blank to disable notifications.
          </p>
          <div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-end">
            <label className="min-w-0 flex-1 text-xs font-medium text-slate-600 dark:text-slate-300">
              <span className="block">Chat ID</span>
              <input
                type="text"
                inputMode="numeric"
                placeholder="123456789"
                value={state.telegramChatId}
                onChange={(event) => preferences.setTelegramChatId(event.target.value)}
                disabled={busy}
                className="mt-2 w-full rounded-xl border border-slate-300 bg-white/90 px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30 disabled:cursor-not-allowed disabled:opacity-60 dark:border-slate-700 dark:bg-slate-950/80 dark:text-white"
              />
            </label>
            <button
              type="button"
              onClick={() => void preferences.sendTestNotification()}
              disabled={!canSendTestNotification(state)}
              className="shrink-0 rounded-xl border border-slate-300 px-4 py-2.5 text-sm font-semibold text-slate-700 transition hover:border-emerald-400 hover:text-emerald-700 disabled:cursor-not-allowed disabled:opacity-50 dark:border-slate-700 dark:text-slate-200 dark:hover:text-emerald-200"
            >
              {state.isTestNotificationSending ? "Sending..." : "Send test"}
            </button>
          </div>
        </div>
      </div>

      <div className="mt-5 flex flex-col gap-4 border-t border-slate-200 pt-5 dark:border-slate-800 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-h-10 flex-1" aria-live="polite">
          {state.settingsError ? (
            <p className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 text-xs text-rose-700 dark:border-rose-900/60 dark:bg-rose-950/30 dark:text-rose-300">
              {state.settingsError}
            </p>
          ) : state.testNotificationMessage ? (
            <p className="rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2.5 text-xs text-emerald-700 dark:border-emerald-900/60 dark:bg-emerald-950/30 dark:text-emerald-300">
              {state.testNotificationMessage}
            </p>
          ) : state.testNotificationError ? (
            <p className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2.5 text-xs text-rose-700 dark:border-rose-900/60 dark:bg-rose-950/30 dark:text-rose-300">
              {state.testNotificationError}
            </p>
          ) : null}
        </div>
        <button
          type="button"
          onClick={() => void preferences.saveSettings()}
          disabled={busy}
          className="shrink-0 rounded-xl bg-emerald-400 px-4 py-2.5 text-sm font-semibold text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
        >
          {state.isSettingsSaving ? "Saving..." : state.isSettingsLoading ? "Loading..." : "Save changes"}
        </button>
      </div>
    </section>
  );
}
