import type { ManualPlatform } from "@/models/manual";
import {
  selectGame,
  setDate,
  setHours,
  setMinutes,
  setMode,
  setPlatform,
  setQuery,
  type SessionCreateMode,
  type SessionCreateState,
} from "../sessionCreateState";
import { BoundSelect, ChangeInput } from "./FormControls";

interface SessionCreateModalProps {
  state: SessionCreateState;
  /** Applies a change to the shared creator state (the Blazor component mutated `State` directly). */
  onStateChange: (updater: (state: SessionCreateState) => SessionCreateState) => void;
  platforms?: readonly ManualPlatform[];
  aiAvailable?: boolean;
  embedded?: boolean;
  onUseAi?: () => void;
  onSearch?: () => void;
  onSave?: () => void;
  onClose?: () => void;
  onQueryChanged?: (value: string) => void;
}

export function SessionCreateModal({
  state,
  onStateChange,
  platforms = [],
  aiAvailable = false,
  embedded = false,
  onUseAi,
  onSearch,
  onSave,
  onClose,
  onQueryChanged,
}: SessionCreateModalProps) {
  const modeClass = (mode: SessionCreateMode) =>
    `rounded-xl px-3 py-2 text-xs font-semibold uppercase tracking-[0.2em] transition ${state.mode === mode ? "bg-white text-emerald-700 shadow-sm dark:bg-slate-800 dark:text-emerald-200" : "text-slate-500 dark:text-slate-400"}`;

  const changeQuery = (value: string) => {
    onStateChange((current) => setQuery(current, value));
    onQueryChanged?.(value);
  };

  const selectedGame = state.selectedGame;
  const sectionClass = embedded
    ? "rounded-3xl border border-slate-200 bg-white/80 p-5 dark:border-slate-800 dark:bg-slate-900/60 sm:p-6"
    : "space-y-4";

  return (
    <div className={embedded ? "w-full" : "fixed inset-0 z-[70] flex items-start justify-center overflow-y-auto bg-slate-950/65 p-4 sm:items-center"}>
      <section
        role={embedded ? undefined : "dialog"}
        aria-modal={embedded ? undefined : "true"}
        aria-labelledby={embedded ? undefined : "session-create-title"}
        data-layout={embedded ? "extended-manual" : undefined}
        className={
          embedded
            ? "w-full text-slate-900 dark:text-white"
            : "my-auto w-full max-w-2xl rounded-3xl border border-slate-200 bg-white p-5 text-slate-900 shadow-2xl dark:border-slate-700 dark:bg-slate-900 dark:text-white sm:p-6"
        }
      >
        {!embedded && (
          <>
            <div className="flex items-start justify-between gap-4">
              <div>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Add session</p>
                <h2 id="session-create-title" className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
                  {state.mode === "live" ? "Start playing now" : "Log a past session"}
                </h2>
              </div>
              <button
                type="button"
                aria-label="Close session creator"
                disabled={state.isSaving}
                onClick={onClose}
                className="rounded-full border border-slate-300 px-3 py-1 text-sm text-slate-600 dark:border-slate-700 dark:text-slate-300"
              >
                ×
              </button>
            </div>

            <div className="mt-5 grid grid-cols-2 gap-2 rounded-2xl bg-slate-100 p-1 dark:bg-slate-950">
              <button
                type="button"
                data-mode="live"
                onClick={() => onStateChange((current) => setMode(current, "live"))}
                className={modeClass("live")}
              >
                Start now
              </button>
              <button
                type="button"
                data-mode="historical"
                onClick={() => onStateChange((current) => setMode(current, "historical"))}
                className={modeClass("historical")}
              >
                Log past session
              </button>
            </div>

            {state.mode === "historical" && aiAvailable && (
              <button type="button" className="mt-4 rounded-full bg-emerald-400 px-4 py-2 text-sm font-semibold text-slate-950" onClick={onUseAi}>
                Use AI
              </button>
            )}
          </>
        )}

        <div className={embedded ? "grid gap-5 lg:grid-cols-[minmax(0,1.35fr)_minmax(20rem,0.65fr)]" : "mt-5 space-y-4"}>
          <section className={sectionClass}>
            {embedded && (
              <>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Game library</p>
                <h3 className="mt-2 text-xl font-semibold">Choose a game</h3>
                <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">
                  Search your saved games and IGDB, then select the exact game you played.
                </p>
              </>
            )}

            <form
              className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-end"
              onSubmit={(event) => {
                event.preventDefault();
                onSearch?.();
              }}
            >
              <label className="flex-1 text-xs font-medium text-slate-600 dark:text-slate-300">
                Game
                <input
                  type="search"
                  autoComplete="off"
                  placeholder="Search saved games and IGDB"
                  value={state.query}
                  onChange={(event) => changeQuery(event.currentTarget.value)}
                  className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950 dark:text-white"
                />
              </label>
              <button
                type="submit"
                disabled={state.isSearching}
                className="rounded-full bg-emerald-400 px-5 py-2.5 text-xs font-semibold uppercase tracking-[0.2em] text-slate-950 disabled:opacity-60"
              >
                {state.isSearching ? "Searching..." : "Search"}
              </button>
            </form>

            {state.searchError.length > 0 && (
              <p role="alert" className="mt-4 text-sm text-rose-700 dark:text-rose-300">
                {state.searchError}
              </p>
            )}
            {!state.isIgdbAvailable && (
              <p className="mt-4 rounded-2xl bg-amber-50 p-3 text-sm text-amber-800 dark:bg-amber-400/10 dark:text-amber-200">
                IGDB search is unavailable. You can still select a saved game.
              </p>
            )}

            {state.results.length > 0 && (
              <div className="mt-4 grid max-h-[30rem] gap-2 overflow-y-auto pr-1 sm:grid-cols-2">
                {state.results.map((result) => (
                  <button
                    type="button"
                    key={result.gameId ?? `igdb-${result.igdbId ?? ""}`}
                    onClick={() => onStateChange((current) => selectGame(current, result))}
                    className={`flex w-full items-center gap-3 rounded-2xl border p-2.5 text-left transition ${state.selectedGame === result ? "border-emerald-400 bg-emerald-50 dark:bg-emerald-400/10" : "border-slate-200 hover:border-emerald-300 dark:border-slate-700"}`}
                  >
                    {result.capsuleUrl !== null ? (
                      <img src={result.capsuleUrl} alt={`${result.title} poster`} className="h-16 w-12 shrink-0 rounded-xl object-cover" />
                    ) : (
                      <span
                        aria-hidden="true"
                        className="flex h-16 w-12 shrink-0 items-center justify-center rounded-xl bg-slate-200 text-[8px] uppercase text-slate-500 dark:bg-slate-800 dark:text-slate-400"
                      >
                        No art
                      </span>
                    )}
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-semibold text-slate-900 dark:text-white">{result.title}</span>
                      <span className="mt-1 block text-xs text-slate-500 dark:text-slate-400">
                        {result.source === "local" ? "Saved game" : "IGDB"}
                      </span>
                    </span>
                  </button>
                ))}
              </div>
            )}

            {selectedGame !== null && (
              <div className="mt-4 flex items-center gap-4 rounded-2xl border border-emerald-300/60 bg-emerald-50 p-3 dark:border-emerald-400/30 dark:bg-emerald-400/10">
                {selectedGame.capsuleUrl !== null ? (
                  <img src={selectedGame.capsuleUrl} alt={`${selectedGame.title} poster`} className="h-20 w-14 rounded-xl object-cover" />
                ) : (
                  <span
                    aria-hidden="true"
                    className="flex h-20 w-14 items-center justify-center rounded-xl bg-slate-200 text-[9px] uppercase text-slate-500 dark:bg-slate-800 dark:text-slate-400"
                  >
                    No art
                  </span>
                )}
                <div className="min-w-0">
                  <p className="text-xs font-semibold uppercase tracking-[0.2em] text-emerald-700 dark:text-emerald-200">Selected game</p>
                  <p className="mt-1 truncate font-semibold text-slate-900 dark:text-white">{selectedGame.title}</p>
                </div>
              </div>
            )}
          </section>

          <section className={sectionClass}>
            {embedded && (
              <>
                <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">Play details</p>
                <h3 className="mt-2 text-xl font-semibold">Session details</h3>
                <p className="mt-1 text-sm text-slate-600 dark:text-slate-300">Add the platform, date, and time played.</p>
              </>
            )}

            <div className="mt-5 space-y-4">
              <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
                Platform
                <BoundSelect
                  data-field="platform"
                  value={state.platformKey}
                  onValueChange={(value) => onStateChange((current) => setPlatform(current, value))}
                  className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none focus:border-emerald-400 dark:border-slate-700 dark:bg-slate-950 dark:text-white"
                >
                  <option value="">Select a platform</option>
                  {platforms.map((platform) => (
                    <option key={platform.key} value={platform.key}>
                      {platform.name}
                    </option>
                  ))}
                </BoundSelect>
              </label>

              {state.mode === "historical" && (
                <>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-300">
                    Date
                    <ChangeInput
                      type="date"
                      value={state.date}
                      onValueChange={(value) => onStateChange((current) => setDate(current, value))}
                      className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 text-sm dark:border-slate-700 dark:bg-slate-950 dark:text-white"
                    />
                  </label>
                  <div className="grid grid-cols-2 gap-3">
                    <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                      Hours
                      <input
                        type="number"
                        min="0"
                        value={state.hours}
                        onChange={(event) => {
                          const value = event.currentTarget.value;
                          onStateChange((current) => setHours(current, value));
                        }}
                        className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 text-sm dark:border-slate-700 dark:bg-slate-950 dark:text-white"
                      />
                    </label>
                    <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                      Minutes
                      <input
                        type="number"
                        min="0"
                        value={state.minutes}
                        onChange={(event) => {
                          const value = event.currentTarget.value;
                          onStateChange((current) => setMinutes(current, value));
                        }}
                        className="mt-2 w-full rounded-xl border border-slate-300 bg-white px-3 py-2.5 text-sm dark:border-slate-700 dark:bg-slate-950 dark:text-white"
                      />
                    </label>
                  </div>
                </>
              )}

              {state.error.length > 0 && (
                <p role="alert" className="rounded-2xl bg-rose-50 p-3 text-sm text-rose-700 dark:bg-rose-400/10 dark:text-rose-200">
                  {state.error}
                </p>
              )}
            </div>

            <div className="mt-6 flex justify-end gap-3 border-t border-slate-200 pt-5 dark:border-slate-800">
              {!embedded && (
                <button
                  type="button"
                  disabled={state.isSaving}
                  onClick={onClose}
                  className="rounded-full border border-slate-300 px-4 py-2 text-xs font-semibold uppercase tracking-[0.2em] dark:border-slate-700"
                >
                  Cancel
                </button>
              )}
              <button
                type="button"
                data-action="save"
                disabled={state.isSaving}
                onClick={onSave}
                className="w-full rounded-full bg-emerald-400 px-5 py-2.5 text-xs font-semibold uppercase tracking-[0.2em] text-slate-950 disabled:opacity-60 sm:w-auto"
              >
                {state.isSaving ? "Saving..." : embedded ? "Save past session" : state.mode === "live" ? "Start session" : "Save session"}
              </button>
            </div>
          </section>
        </div>
      </section>
    </div>
  );
}
