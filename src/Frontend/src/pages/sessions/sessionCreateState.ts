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
  return { ...state, query, results: [], selectedGame: null, searchError: "", error: "" };
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
    searchError: "",
  };
}

export function failSearch(state: SessionCreateState, error: string): SessionCreateState {
  return { ...state, results: [], isSearching: false, searchError: error };
}

export function cancelSearch(state: SessionCreateState): SessionCreateState {
  return { ...state, isSearching: false };
}

export function selectGame(state: SessionCreateState, game: ManualMappingSearchResult): SessionCreateState {
  return { ...state, selectedGame: game, platformKey: "", error: "" };
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

/** Whole-minute duration, or the validation message. */
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

  // (int)Math.Floor(...) saturates in .NET.
  const durationMinutes = Math.min(Math.floor(hours * 60 + minutes), 2_147_483_647);
  return durationMinutes > 0 ? durationMinutes : "Duration must be at least 1 minute.";
}
