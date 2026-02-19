import { createPortal } from "react-dom";
import type { ManualMappingSearchResult } from "../types";
import { formatEntryResultLabel } from "../utils/formatters";

type ManualEntryModalProps = {
  isOpen: boolean;
  manualEntryDate: string;
  manualEntryHours: string;
  manualEntryMinutes: string;
  manualEntrySearchQuery: string;
  manualEntrySearchResults: ManualMappingSearchResult[];
  manualEntrySelection: ManualMappingSearchResult | null;
  manualEntryPlatformKey: string;
  manualEntryError: string;
  manualEntrySearchError: string;
  manualEntryIgdbAvailable: boolean;
  isManualEntrySearchLoading: boolean;
  isManualEntrySaving: boolean;
  platformOptions: { key: string; label: string }[];
  onClose: () => void;
  onSearch: () => void;
  onSave: () => void;
  onDateChange: (value: string) => void;
  onHoursChange: (value: string) => void;
  onMinutesChange: (value: string) => void;
  onSearchQueryChange: (value: string) => void;
  onSelectResult: (result: ManualMappingSearchResult) => void;
  onPlatformKeyChange: (value: string) => void;
};

export const ManualEntryModal = ({
  isOpen,
  manualEntryDate,
  manualEntryHours,
  manualEntryMinutes,
  manualEntrySearchQuery,
  manualEntrySearchResults,
  manualEntrySelection,
  manualEntryPlatformKey,
  manualEntryError,
  manualEntrySearchError,
  manualEntryIgdbAvailable,
  isManualEntrySearchLoading,
  isManualEntrySaving,
  platformOptions,
  onClose,
  onSearch,
  onSave,
  onDateChange,
  onHoursChange,
  onMinutesChange,
  onSearchQueryChange,
  onSelectResult,
  onPlatformKeyChange,
}: ManualEntryModalProps) => {
  if (!isOpen || typeof document === "undefined") {
    return null;
  }

  const localResults = manualEntrySearchResults.filter(
    (result) => result.source === "local"
  );
  const igdbResults = manualEntrySearchResults.filter(
    (result) => result.source === "igdb"
  );

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto bg-slate-900/30 px-4 py-4 dark:bg-slate-950/80 sm:items-center sm:py-8">
      <div className="my-auto flex w-full max-w-2xl flex-col rounded-3xl border border-slate-200 bg-white/90 p-5 shadow-[0_30px_80px_rgba(15,23,42,0.7)] dark:border-slate-800 dark:bg-slate-900/90 sm:p-6 max-h-[calc(100dvh-2rem)]">
        <div className="flex items-center justify-between gap-4">
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald-700 dark:text-emerald-300">
              Manual entry
            </p>
            <h2 className="mt-2 text-xl font-semibold text-slate-900 dark:text-white">
              Log a time entry
            </h2>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-full border border-slate-300 dark:border-slate-700 px-3 py-1 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200"
          >
            Close
          </button>
        </div>

        <div className="mt-6 grid min-h-0 gap-4 overflow-y-auto pr-1">
          <div className="grid gap-3 sm:grid-cols-[0.7fr_0.3fr]">
            <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
              Date
              <input
                type="date"
                value={manualEntryDate}
                onChange={(event) => onDateChange(event.target.value)}
                className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
              />
            </label>
            <div className="grid grid-cols-2 gap-3">
              <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                Hours
                <input
                  type="number"
                  min="0"
                  step="1"
                  value={manualEntryHours}
                  onChange={(event) => onHoursChange(event.target.value)}
                  className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                />
              </label>
              <label className="text-xs font-medium text-slate-600 dark:text-slate-300">
                Minutes
                <input
                  type="number"
                  min="0"
                  step="1"
                  value={manualEntryMinutes}
                  onChange={(event) => onMinutesChange(event.target.value)}
                  className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                />
              </label>
            </div>
          </div>

          <div className="flex flex-wrap items-end gap-3">
            <label className="flex-1 text-xs font-medium text-slate-600 dark:text-slate-300">
              Search by name
              <input
                type="text"
                value={manualEntrySearchQuery}
                onChange={(event) => onSearchQueryChange(event.target.value)}
                placeholder="Search games"
                className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
              />
            </label>
            <button
              type="button"
              onClick={onSearch}
              disabled={isManualEntrySearchLoading}
              className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
            >
              {isManualEntrySearchLoading ? "Searching..." : "Search"}
            </button>
          </div>

          {!manualEntryIgdbAvailable ? (
            <p className="text-xs text-amber-700 dark:text-amber-200">
              IGDB is not configured on the server.
            </p>
          ) : null}

          {manualEntrySearchError ? (
            <p className="text-xs text-rose-700 dark:text-rose-300">{manualEntrySearchError}</p>
          ) : null}

          <div className="grid gap-3 lg:grid-cols-2">
            <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
              <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                Existing games
              </p>
              <div className="mt-3 max-h-64 space-y-2 overflow-y-auto pr-1">
                {localResults.length === 0 ? (
                  <p className="text-xs text-slate-500 dark:text-slate-400">
                    Search to see saved games.
                  </p>
                ) : (
                  localResults.map((result) => (
                    <button
                      key={`local-${result.gameId}`}
                      type="button"
                      onClick={() => onSelectResult(result)}
                      className={`flex w-full items-center justify-between gap-3 rounded-xl border px-3 py-2 text-left text-sm transition ${
                        manualEntrySelection?.gameId === result.gameId
                          ? "border-emerald-400/60 bg-emerald-400/10 text-emerald-700 dark:text-emerald-100"
                          : "border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 text-slate-700 dark:text-slate-200 hover:border-emerald-400/40"
                      }`}
                    >
                      <span>{formatEntryResultLabel(result)}</span>
                      <span className="text-[10px] uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                        Local
                      </span>
                    </button>
                  ))
                )}
              </div>
            </div>

            <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
              <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                IGDB results
              </p>
              <div className="mt-3 max-h-64 space-y-2 overflow-y-auto pr-1">
                {igdbResults.length === 0 ? (
                  <p className="text-xs text-slate-500 dark:text-slate-400">
                    Search to see IGDB matches.
                  </p>
                ) : (
                  igdbResults.map((result) => (
                    <button
                      key={`igdb-${result.igdbId}`}
                      type="button"
                      onClick={() => onSelectResult(result)}
                      className={`flex w-full items-center justify-between gap-3 rounded-xl border px-3 py-2 text-left text-sm transition ${
                        manualEntrySelection?.igdbId === result.igdbId
                          ? "border-emerald-400/60 bg-emerald-400/10 text-emerald-700 dark:text-emerald-100"
                          : "border-slate-200 dark:border-slate-800 bg-slate-200/40 dark:bg-slate-950/40 text-slate-700 dark:text-slate-200 hover:border-emerald-400/40"
                      }`}
                    >
                      <span>{formatEntryResultLabel(result)}</span>
                    </button>
                  ))
                )}
              </div>
            </div>
          </div>

          {manualEntrySelection ? (
            <div className="rounded-2xl border border-slate-200 dark:border-slate-800 bg-slate-200/50 dark:bg-slate-950/50 p-4">
              <p className="text-xs uppercase tracking-[0.3em] text-slate-500 dark:text-slate-400">
                Selected game
              </p>
              <p className="mt-2 text-sm font-semibold text-slate-900 dark:text-white">
                {formatEntryResultLabel(manualEntrySelection)}
              </p>
              {manualEntrySelection.source === "igdb" ? (
                <label className="mt-4 block text-xs font-medium text-slate-600 dark:text-slate-300">
                  Choose platform
                  <select
                    value={manualEntryPlatformKey}
                    onChange={(event) =>
                      onPlatformKeyChange(event.target.value)
                    }
                    className="mt-2 w-full rounded-xl border border-slate-300 dark:border-slate-700 bg-white/90 dark:bg-slate-950/80 px-3 py-2 text-sm text-slate-900 dark:text-white outline-none transition focus:border-emerald-400 focus:ring-2 focus:ring-emerald-400/30"
                  >
                    <option value="">Select a platform</option>
                    {platformOptions.map((platform) => (
                      <option key={platform.key} value={platform.key}>
                        {platform.label}
                      </option>
                    ))}
                  </select>
                </label>
              ) : null}
            </div>
          ) : null}

          {manualEntryError ? (
            <p className="text-xs text-rose-700 dark:text-rose-300">{manualEntryError}</p>
          ) : null}
        </div>

        <div className="mt-6 flex shrink-0 flex-wrap items-center justify-end gap-3">
          <button
            type="button"
            onClick={onClose}
            className="rounded-full border border-slate-300 dark:border-slate-700 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-700 dark:text-slate-200"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={onSave}
            disabled={isManualEntrySaving}
            className="rounded-full bg-emerald-400 px-4 py-2 text-xs font-semibold uppercase tracking-[0.3em] text-slate-900 transition hover:bg-emerald-300 disabled:cursor-not-allowed disabled:bg-emerald-300/60"
          >
            {isManualEntrySaving ? "Saving..." : "Save entry"}
          </button>
        </div>
      </div>
    </div>,
    document.body
  );
};
