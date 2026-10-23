import { useId, useRef } from "react";
import { Cover, Dot } from "@/components/ui";
import { cx } from "@/lib/cx";
import { dialogKeyDown, useDialogFocus } from "@/lib/focusTrap";
import { formatCountNoun } from "@/lib/format";
import { platformColor } from "@/lib/platforms";
import type { ManualMappingSearchResult, ManualPlatform } from "@/models/manual";
import {
  clearGame,
  formatMinutes,
  selectGame,
  setDate,
  setHours,
  setMinutes,
  setMode,
  setPlatform,
  setQuery,
  submissionBlocker,
  tryBuildSubmission,
  type SessionCreateMode,
  type SessionCreateState,
} from "../sessionCreateState";
import { ChangeInput } from "./FormControls";

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

type Update = SessionCreateModalProps["onStateChange"];

const durationPresets: ReadonlyArray<{ hours: number; minutes: number }> = [
  { hours: 0, minutes: 15 },
  { hours: 0, minutes: 30 },
  { hours: 1, minutes: 0 },
  { hours: 2, minutes: 0 },
];

/** Local calendar date `offset` days from today as yyyy-MM-dd. */
function localDate(offset: number): string {
  const date = new Date();
  date.setDate(date.getDate() + offset);
  return `${String(date.getFullYear()).padStart(4, "0")}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

/** Submission messages for a field that simply has not been filled in yet, phrased as the next step. */
const missingFieldPrompts: Readonly<Record<string, string>> = {
  "Select a game.": "Choose a game to continue.",
  "Select a platform.": "Choose a platform to continue.",
  "Select a date.": "Choose a date to continue.",
};

function shortDate(value: string): string {
  const date = new Date(`${value}T12:00`);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString("en-US", { month: "short", day: "numeric" });
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
  const dialogRef = useRef<HTMLElement>(null);
  useDialogFocus(dialogRef, !embedded);

  const changeQuery = (value: string) => {
    onStateChange((current) => setQuery(current, value));
    onQueryChanged?.(value);
  };

  const gameStep = (
    <GameStep state={state} embedded={embedded} onStateChange={onStateChange} onQueryChange={changeQuery} onSearch={onSearch} />
  );
  const detailsStep = (
    <DetailsStep state={state} embedded={embedded} platforms={platforms} onStateChange={onStateChange} onSave={onSave} onClose={onClose} />
  );

  if (embedded) {
    return (
      <div data-layout="extended-manual" className="grid w-full grid-cols-[repeat(auto-fit,minmax(min(360px,100%),1fr))] items-start gap-7">
        <section className="pdx-card flex flex-col gap-[22px]">{gameStep}</section>
        <section className="pdx-card flex flex-col gap-[22px]">{detailsStep}</section>
      </div>
    );
  }

  const modeButton = (mode: SessionCreateMode, label: string) => (
    <button
      type="button"
      data-mode={mode}
      aria-pressed={state.mode === mode}
      onClick={() => onStateChange((current) => setMode(current, mode))}
      className="pdx-seg-btn h-[38px] flex-1 px-[18px]"
    >
      {label}
    </button>
  );

  return (
    <div
      className="pdx-modal-backdrop fixed inset-0 z-[70] flex items-start justify-center overflow-y-auto p-4 sm:items-center"
      onKeyDown={(event) => dialogKeyDown(event, dialogRef.current, state.isSaving ? undefined : onClose)}
    >
      <section
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="session-create-title"
        className="pdx-modal my-auto flex w-full max-w-2xl flex-col gap-[22px] text-ink"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <p className="pdx-kicker">Add session</p>
            <h2 id="session-create-title" className="pdx-title">
              {state.mode === "live" ? "Start playing now" : "Log a past session"}
            </h2>
          </div>
          <button type="button" aria-label="Close session creator" disabled={state.isSaving} onClick={onClose} className="pdx-icon-btn">
            ✕
          </button>
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <div className="pdx-seg flex-1">
            {modeButton("live", "Start now")}
            {modeButton("historical", "Log past session")}
          </div>
          {state.mode === "historical" && aiAvailable && (
            <button type="button" className="pdx-btn-accent" onClick={onUseAi}>
              Use AI
            </button>
          )}
        </div>

        {gameStep}
        <div className="flex flex-col gap-[22px] border-t border-divider pt-[22px]">{detailsStep}</div>
      </section>
    </div>
  );
}

function GameStep({
  state,
  embedded,
  onStateChange,
  onQueryChange,
  onSearch,
}: {
  state: SessionCreateState;
  embedded: boolean;
  onStateChange: Update;
  onQueryChange: (value: string) => void;
  onSearch?: () => void;
}) {
  const resultCount = state.results.length;
  return (
    <>
      {embedded && (
        <div>
          <p className="pdx-kicker">Step 1 · Game</p>
          <h2 className="pdx-title">Choose a game</h2>
          <p className="mt-1.5 text-sm text-muted">Search your saved games and IGDB, then select the exact game you played.</p>
        </div>
      )}

      <form
        role="search"
        className="relative"
        onSubmit={(event) => {
          event.preventDefault();
          onSearch?.();
        }}
      >
        <span aria-hidden="true" className="pointer-events-none absolute left-5 top-1/2 -translate-y-1/2 text-base text-faint">
          ⌕
        </span>
        <input
          type="search"
          aria-label="Game"
          autoComplete="off"
          placeholder="Search saved games and IGDB"
          value={state.query}
          onChange={(event) => onQueryChange(event.currentTarget.value)}
          className="pdx-input h-[52px] pl-[46px] pr-[118px] text-[15px]"
        />
        <button type="submit" disabled={state.isSearching} className="pdx-btn-primary absolute right-1.5 top-1.5 px-[18px]">
          {state.isSearching ? "Searching…" : "Search"}
        </button>
      </form>

      {state.searchError.length > 0 && (
        <p role="alert" className="text-sm font-medium text-danger">
          {state.searchError}
        </p>
      )}
      {!state.isIgdbAvailable && <p className="pdx-dashed text-warn">IGDB search is unavailable. You can still select a saved game.</p>}

      {resultCount > 0 ? (
        <>
          <div className="flex items-baseline justify-between gap-3">
            <span className="pdx-label">Matches</span>
            <span className="pdx-num text-xs text-muted">{formatCountNoun(resultCount, "result")}</span>
          </div>
          <div className="grid grid-cols-[repeat(auto-fill,minmax(min(230px,100%),1fr))] gap-2.5">
            {state.results.map((result) => (
              <ResultButton
                key={result.gameId ?? `igdb-${result.igdbId ?? ""}`}
                result={result}
                selected={state.selectedGame === result}
                onSelect={() => onStateChange((current) => selectGame(current, result))}
              />
            ))}
          </div>
        </>
      ) : (
        state.searchError.length === 0 &&
        (state.hasSearched && !state.isSearching ? (
          <p className="pdx-dashed rounded-[20px] text-sm [overflow-wrap:anywhere]">
            Nothing matches “{state.query.trim()}”. Try a shorter name.
          </p>
        ) : (
          embedded && <p className="pdx-dashed rounded-[20px] text-sm">Search by title, then pick the exact game from the matches.</p>
        ))
      )}
    </>
  );
}

function ResultButton({ result, selected, onSelect }: { result: ManualMappingSearchResult; selected: boolean; onSelect: () => void }) {
  const local = result.source === "local";
  return (
    <button
      type="button"
      aria-pressed={selected}
      onClick={onSelect}
      className={cx(
        "grid grid-cols-[48px_minmax(0,1fr)_auto] items-center gap-3 rounded-[20px] border-[1.5px] py-2.5 pl-2.5 pr-3.5 text-left transition-colors",
        selected ? "border-bar bg-mint" : "border-transparent bg-tile hover:border-mint-hover",
      )}
    >
      <Cover title={result.title} src={result.capsuleUrl} className="h-12 w-12 rounded-[14px]" />
      <span className="min-w-0">
        <span title={result.title} className="line-clamp-2 text-sm font-medium leading-[1.3] [overflow-wrap:anywhere]">
          {result.title}
        </span>
        <span className={cx("mt-[3px] block text-[11px] font-semibold uppercase tracking-[0.16em]", local ? "text-accent" : "text-muted")}>
          {local ? "Saved game" : "From IGDB"}
        </span>
      </span>
      <span
        aria-hidden="true"
        className={cx(
          "grid h-6 w-6 place-items-center rounded-full text-xs font-bold text-white",
          selected ? "bg-brand" : "bg-transparent",
        )}
      >
        {selected ? "✓" : ""}
      </span>
    </button>
  );
}

function DetailsStep({
  state,
  embedded,
  platforms,
  onStateChange,
  onSave,
  onClose,
}: {
  state: SessionCreateState;
  embedded: boolean;
  platforms: readonly ManualPlatform[];
  onStateChange: Update;
  onSave?: () => void;
  onClose?: () => void;
}) {
  const ids = useId();
  const selectedGame = state.selectedGame;
  const historical = state.mode === "historical";
  const blocker = submissionBlocker(state);
  const platformName = platforms.find((platform) => platform.key === state.platformKey)?.name ?? state.platformKey;
  const submission = blocker === null ? tryBuildSubmission(state).submission : null;
  const missing = blocker === null ? null : (missingFieldPrompts[blocker] ?? null);
  const summary =
    submission === null || selectedGame === null
      ? (missing ?? blocker)
      : submission.kind === "entry"
        ? `${selectedGame.title} · ${platformName} · ${formatMinutes(submission.body.durationMinutes)} on ${shortDate(state.date)}`
        : `${selectedGame.title} · ${platformName} · starts now`;
  // Missing fields read as a quiet prompt; an invalid duration (over 24 hours too) or a future date as an error.
  const summaryTone = missing !== null ? "text-muted" : blocker !== null ? "text-danger" : "text-ink";
  const pickDuration = (hours: number, minutes: number) =>
    onStateChange((current) => setMinutes(setHours(current, String(hours)), String(minutes)));

  return (
    <>
      {embedded && (
        <div>
          <p className="pdx-kicker">Step 2 · Details</p>
          <h2 className="pdx-title">Session details</h2>
        </div>
      )}

      {selectedGame !== null ? (
        <div className="flex items-center gap-3.5 rounded-[20px] bg-mint px-3.5 py-3">
          <Cover
            title={selectedGame.title}
            src={selectedGame.capsuleUrl}
            className="h-[52px] w-[52px] rounded-[14px]"
            initialClassName="text-[17px]"
          />
          <div className="min-w-0 flex-1">
            <p title={selectedGame.title} className="truncate text-[15px] font-semibold">
              {selectedGame.title}
            </p>
            <p className="mt-0.5 text-[11px] font-semibold uppercase tracking-[0.16em] text-accent">Selected</p>
          </div>
          <button
            type="button"
            aria-label="Clear selected game"
            onClick={() => onStateChange(clearGame)}
            className="grid h-8 w-8 shrink-0 place-items-center rounded-full bg-surface text-sm font-semibold text-ink-2"
          >
            ✕
          </button>
        </div>
      ) : (
        <p className="pdx-dashed rounded-[20px] px-5 text-sm">Pick a game from the list first.</p>
      )}

      <div className="flex flex-col gap-2.5">
        <span id={`${ids}-platform`} className="text-xs font-medium text-muted">
          Platform
        </span>
        <div role="group" aria-labelledby={`${ids}-platform`} data-field="platform" className="flex flex-wrap gap-1.5">
          {platforms.map((platform) => (
            <button
              key={platform.key}
              type="button"
              data-platform={platform.key}
              aria-pressed={state.platformKey === platform.key}
              onClick={() => onStateChange((current) => setPlatform(current, platform.key))}
              className="pdx-chip h-[38px] pl-2.5 pr-3.5 font-semibold"
            >
              <Dot color={platformColor(platform.key, platform.name)} />
              {platform.name}
            </button>
          ))}
          {platforms.length === 0 && <span className="text-sm text-muted">No platforms are available.</span>}
        </div>
      </div>

      {historical && (
        <>
          <div className="flex flex-col gap-2.5">
            <label htmlFor={`${ids}-date`} className="text-xs font-medium text-muted">
              Date
            </label>
            <div className="flex flex-wrap gap-2">
              <ChangeInput
                id={`${ids}-date`}
                type="date"
                max={localDate(0)}
                value={state.date}
                onValueChange={(value) => onStateChange((current) => setDate(current, value))}
                className="pdx-input h-[46px] flex-[1_1_160px]"
              />
              {[
                { label: "Today", value: localDate(0) },
                { label: "Yesterday", value: localDate(-1) },
              ].map((chip) => (
                <button
                  key={chip.label}
                  type="button"
                  aria-pressed={state.date === chip.value}
                  onClick={() => onStateChange((current) => setDate(current, chip.value))}
                  className="pdx-chip h-[46px] font-semibold"
                >
                  {chip.label}
                </button>
              ))}
            </div>
          </div>

          <div className="flex flex-col gap-2.5">
            <span className="text-xs font-medium text-muted">Time played</span>
            <div className="flex flex-wrap items-center gap-2">
              <DurationField
                label="Hours"
                unit="h"
                max={99}
                widthClassName="w-[52px]"
                value={state.hours}
                onChange={(value) => onStateChange((current) => setHours(current, value))}
              />
              <DurationField
                label="Minutes"
                unit="min"
                max={59}
                widthClassName="w-11"
                value={state.minutes}
                onChange={(value) => onStateChange((current) => setMinutes(current, value))}
              />
              {durationPresets.map((preset) => (
                <button
                  key={`${preset.hours}-${preset.minutes}`}
                  type="button"
                  aria-pressed={Number(state.hours) === preset.hours && Number(state.minutes) === preset.minutes}
                  onClick={() => pickDuration(preset.hours, preset.minutes)}
                  className="pdx-chip h-[34px] px-3 text-xs font-semibold"
                >
                  {formatMinutes(preset.hours * 60 + preset.minutes)}
                </button>
              ))}
            </div>
          </div>
        </>
      )}

      {state.error.length > 0 && (
        <p role="alert" className="rounded-[20px] border border-danger-line bg-danger-tint px-4 py-3 text-sm text-danger">
          {state.error}
        </p>
      )}

      <div className="flex flex-col gap-3 border-t border-divider pt-5">
        <p id={`${ids}-summary`} aria-live="polite" className={cx("text-[13px] [overflow-wrap:anywhere]", summaryTone)}>
          {summary}
        </p>
        <div className="flex flex-wrap gap-3">
          {!embedded && (
            <button type="button" disabled={state.isSaving} onClick={onClose} className="pdx-btn-outline h-[52px] flex-1">
              Cancel
            </button>
          )}
          <button
            type="button"
            data-action="save"
            aria-describedby={`${ids}-summary`}
            disabled={state.isSaving || blocker !== null}
            onClick={onSave}
            className="pdx-btn-primary h-[52px] flex-[2_1_auto] text-[15px]"
          >
            {state.isSaving ? "Saving…" : state.mode === "live" ? "Start session" : embedded ? "Save past session" : "Save session"}
          </button>
        </div>
      </div>
    </>
  );
}

function DurationField({
  label,
  unit,
  max,
  widthClassName,
  value,
  onChange,
}: {
  label: string;
  unit: string;
  max: number;
  widthClassName: string;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <label className="flex h-[46px] items-center rounded-full border border-line bg-surface pl-[18px] pr-1.5 transition-colors focus-within:border-bar">
      <span className="sr-only">{label}</span>
      <input
        type="number"
        min="0"
        max={max}
        value={value}
        onChange={(event) => onChange(event.currentTarget.value)}
        className={cx("pdx-num border-0 bg-transparent text-base font-semibold text-ink outline-none", widthClassName)}
      />
      <span aria-hidden="true" className="pr-2.5 text-[13px] text-muted">
        {unit}
      </span>
    </label>
  );
}
