import type {
  ManualEntryCreateRequest,
  ManualMappingSearchResponse,
  ManualMappingSearchResult,
  ManualSessionStartRequest,
} from "@/models/manual";
import { escapeDataString } from "./support";

export type SessionCreateMode = "live" | "historical";

export type SessionCreateSubmission =
  | { path: "/manual/sessions/start"; method: "POST"; kind: "start"; body: ManualSessionStartRequest }
  | { path: "/manual/entries"; method: "POST"; kind: "entry"; body: ManualEntryCreateRequest };

export interface SessionCreateState {
  isOpen: boolean;
  mode: SessionCreateMode;
  query: string;
  results: readonly ManualMappingSearchResult[];
  selectedGame: ManualMappingSearchResult | null;
  platformKey: string;
  date: string;
  hours: string;
  minutes: string;
  error: string;
  searchError: string;
  isSearching: boolean;
  /** True once a search for the current query has answered, so "no matches" can be told apart from "not searched yet". */
  hasSearched: boolean;
  isSaving: boolean;
  isIgdbAvailable: boolean;
}

export function createSessionCreateState(): SessionCreateState {
  return {
    isOpen: false,
    mode: "live",
    query: "",
    results: [],
    selectedGame: null,
    platformKey: "",
    date: "",
    hours: "1",
    minutes: "0",
    error: "",
    searchError: "",
    isSearching: false,
    hasSearched: false,
    isSaving: false,
    isIgdbAvailable: true,
  };
}

/** Opens a fresh form; the date defaults to the local calendar date of `now`. */
export function openSessionCreate(mode: SessionCreateMode, now: Date): SessionCreateState {
  const date = `${String(now.getFullYear()).padStart(4, "0")}-${String(now.getMonth() + 1).padStart(2, "0")}-${String(now.getDate()).padStart(2, "0")}`;
  return { ...createSessionCreateState(), isOpen: true, mode, date };
}

export const closeSessionCreate = createSessionCreateState;

export function setMode(state: SessionCreateState, mode: SessionCreateMode): SessionCreateState {
  return { ...state, mode, error: "" };
}

export function setQuery(state: SessionCreateState, query: string): SessionCreateState {
  return { ...state, query, results: [], selectedGame: null, searchError: "", error: "", hasSearched: false };
}

export function tryBeginSearch(state: SessionCreateState): { state: SessionCreateState; path: string | null } {
  const query = state.query.trim();
  if (query.length === 0) {
    return { state: { ...state, searchError: "Enter a game title." }, path: null };
  }
  return {
    state: { ...state, isSearching: true, searchError: "" },
    path: `/manual/search?query=${escapeDataString(query)}`,
  };
}

export function completeSearch(state: SessionCreateState, response: ManualMappingSearchResponse): SessionCreateState {
  return {
    ...state,
    results: response.results,
    isIgdbAvailable: response.igdbAvailable,
    isSearching: false,
    hasSearched: true,
    searchError: "",
  };
}

export function failSearch(state: SessionCreateState, error: string): SessionCreateState {
  return { ...state, results: [], isSearching: false, hasSearched: false, searchError: error };
}

export function cancelSearch(state: SessionCreateState): SessionCreateState {
  return { ...state, isSearching: false };
}

export function selectGame(state: SessionCreateState, game: ManualMappingSearchResult): SessionCreateState {
  return { ...state, selectedGame: game, platformKey: "", error: "" };
}

/** Clears the chosen game (the "✕" on the selected-game strip); the platform stays. */
export function clearGame(state: SessionCreateState): SessionCreateState {
  return { ...state, selectedGame: null, error: "" };
}

export function setPlatform(state: SessionCreateState, platformKey: string): SessionCreateState {
  return { ...state, platformKey, error: "" };
}

export function setDate(state: SessionCreateState, date: string): SessionCreateState {
  return { ...state, date };
}

export function setHours(state: SessionCreateState, hours: string): SessionCreateState {
  return { ...state, hours };
}

export function setMinutes(state: SessionCreateState, minutes: string): SessionCreateState {
  return { ...state, minutes };
}

export function tryBuildSubmission(state: SessionCreateState): {
  state: SessionCreateState;
  submission: SessionCreateSubmission | null;
} {
  const fail = (error: string) => ({ state: { ...state, error }, submission: null });
  const game = state.selectedGame;
  if (game === null) return fail("Select a game.");
  if (state.platformKey.trim().length === 0) return fail("Select a platform.");

  const cleared = { ...state, error: "" };
  if (state.mode === "live") {
    return {
      state: cleared,
      submission: {
        path: "/manual/sessions/start",
        method: "POST",
        kind: "start",
        body: {
          title: game.title,
          gameId: game.gameId,
          platformKey: state.platformKey,
          igdbId: game.igdbId,
          capsuleUrl: game.capsuleUrl,
        },
      },
    };
  }

  if (state.date.length === 0) return fail("Select a date.");
  const duration = parseDuration(state.hours, state.minutes);
  if (typeof duration === "string") return fail(duration);

  return {
    state: cleared,
    submission: {
      path: "/manual/entries",
      method: "POST",
      kind: "entry",
      body: {
        date: state.date,
        durationMinutes: duration,
        gameId: game.gameId,
        igdbId: game.igdbId,
        title: game.title,
        platformKey: state.platformKey,
        capsuleUrl: game.capsuleUrl,
      },
    },
  };
}

/**
 * The validation message that currently blocks saving, or null when the form is complete.
 * Uses the same rules as `tryBuildSubmission`, so the disabled save button and the server request agree.
 */
export function submissionBlocker(state: SessionCreateState): string | null {
  const built = tryBuildSubmission(state);
  return built.submission === null ? built.state.error : null;
}

/** Whole minutes as "2h 30m", "45m" or "1h". */
export function formatMinutes(totalMinutes: number): string {
  const hours = Math.floor(totalMinutes / 60);
  const minutes = totalMinutes % 60;
  if (hours === 0) return `${minutes}m`;
  return minutes === 0 ? `${hours}h` : `${hours}h ${minutes}m`;
}

/** Returns null when a save is already in flight (TryBeginSave == false). */
export function tryBeginSave(state: SessionCreateState): SessionCreateState | null {
  if (state.isSaving) return null;
  return { ...state, isSaving: true, error: "" };
}

export function failSave(state: SessionCreateState, error: string): SessionCreateState {
  return { ...state, isSaving: false, error };
}

export const completeSave = createSessionCreateState;

/** `double.TryParse(value, NumberStyles.Float, InvariantCulture)`. */
export function parseInvariantDouble(value: string): number | null {
  const trimmed = value.trim();
  if (/^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?$/.test(trimmed)) return Number(trimmed);
  if (/^[+-]?(Infinity|∞)$/.test(trimmed)) return trimmed.startsWith("-") ? -Infinity : Infinity;
  if (trimmed === "NaN") return NaN;
  return null;
}

/** Durations above this many minutes (24h) are allowed, but only after a second look. */
export const LongDurationMinutes = 24 * 60;

/** True for a valid duration over 24 hours, which the form flags before saving. */
export function isLongDuration(totalMinutes: number): boolean {
  return totalMinutes > LongDurationMinutes;
}

/** Whole-minute duration, or the validation message. Zero is rejected, minutes stay under 60, hours at most 99. */
function parseDuration(hoursText: string, minutesText: string): number | string {
  const hours = parseInvariantDouble(hoursText);
  const minutes = parseInvariantDouble(minutesText);
  if (
    hours === null ||
    minutes === null ||
    !Number.isFinite(hours) ||
    !Number.isFinite(minutes) ||
    hours < 0 ||
    minutes < 0
  ) {
    return "Enter a valid duration.";
  }
  if (minutes > 59) return "Minutes must be between 0 and 59.";
  if (hours > 99) return "Hours must be 99 or less.";

  const durationMinutes = Math.floor(hours * 60 + minutes);
  return durationMinutes > 0 ? durationMinutes : "Add how long you played.";
}
