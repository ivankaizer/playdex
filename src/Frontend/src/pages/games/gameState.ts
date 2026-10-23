import { withTimeZone } from "@/lib/dates";
import { formatDateLabel } from "@/lib/format";
import { decodeGameHash } from "@/lib/gameHash";
import type { HttpMethod } from "@/lib/api";
import type { GameIgnoreResponse, GameRatingResponse, GameResponse, GameStatsResponse } from "@/models/games";
import type { AccountSession, AccountSessionsResponse } from "@/models/sessions";

const RecentPageSize = 5;
const InvalidGameLink = "Invalid game link.";
const MonthShort = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

export interface GameRequests {
  gameId: string;
  gameHash: string;
  details: string;
  sessionsBase: string;
  stats: string;
  ignoreStatus: string;
  rating: string;
}

/** Every session of the game, however long ago; years and months of the stats follow the viewer's calendar. */
export function gameRequestsFor(gameHash: string): GameRequests {
  const gameId = decodeGameHash(gameHash);
  if (gameId === null) throw new Error("Invalid game hash.");
  return {
    gameId: String(gameId),
    gameHash,
    details: `/games/${gameHash}`,
    sessionsBase: `/sessions?gameId=${gameHash}&sort=date&direction=desc`,
    stats: withTimeZone(`/games/${gameHash}/stats`),
    ignoreStatus: `/games/ignored?gameId=${gameHash}`,
    rating: `/games/rating?gameId=${gameHash}`,
  };
}

export function sessionsPagePath(requests: GameRequests, page: number): string {
  return `${requests.sessionsBase}&limit=5&offset=${(page - 1) * 5}`;
}

export interface GameMutationSubmission {
  path: string;
  method: HttpMethod;
  body: Record<string, string | number>;
}

export interface GameChartBucket {
  key: string;
  label: string;
  year: number;
  month: number;
  seconds: number;
  hours: number;
}

export interface GameState {
  now: Date;
  requests: GameRequests | null;
  gameDetails: GameResponse | null;
  gameDetailsError: string;
  recentSessions: AccountSession[];
  isLoading: boolean;
  gameError: string;
  sessionsLoaded: boolean;
  isIgnoreChecking: boolean;
  isIgnoreSaving: boolean;
  isIgnored: boolean;
  ignoreError: string;
  rating: number | null;
  isRatingLoading: boolean;
  isRatingSaving: boolean;
  ratingError: string;
  selectedYear: number;
  recentPage: number;
  recentTotal: number;
  isRecentPageLoading: boolean;
  stats: GameStatsResponse | null;
  statsError: string;
}

/** Mirrors GameState.TryBegin: resets everything and resolves the requests for a (normalized) hash. */
export function beginGame(gameHash: string | null, now: Date): GameState {
  const base: GameState = {
    now,
    requests: null,
    gameDetails: null,
    gameDetailsError: "",
    recentSessions: [],
    isLoading: false,
    gameError: "",
    sessionsLoaded: false,
    isIgnoreChecking: false,
    isIgnoreSaving: false,
    isIgnored: false,
    ignoreError: "",
    rating: null,
    isRatingLoading: false,
    isRatingSaving: false,
    ratingError: "",
    selectedYear: now.getFullYear(),
    recentPage: 1,
    recentTotal: 0,
    isRecentPageLoading: false,
    stats: null,
    statsError: "",
  };

  if (gameHash === null || gameHash.trim().length === 0 || decodeGameHash(gameHash) === null) {
    return {
      ...base,
      gameDetailsError: InvalidGameLink,
      gameError: InvalidGameLink,
      ignoreError: InvalidGameLink,
      ratingError: InvalidGameLink,
      sessionsLoaded: true,
    };
  }

  return {
    ...base,
    requests: gameRequestsFor(gameHash),
    isLoading: true,
    sessionsLoaded: false,
    isIgnoreChecking: true,
    isRatingLoading: true,
  };
}

export const completeDetails = (state: GameState, response: GameResponse): GameState => ({
  ...state,
  gameDetails: response,
});

export const failDetails = (state: GameState, error: string): GameState => ({
  ...state,
  gameDetails: null,
  gameDetailsError: error,
});

export const beginFirstSessionsLoad = (state: GameState): GameState => ({ ...state, isLoading: true, gameError: "" });

export const completeFirstSessions = (state: GameState, response: AccountSessionsResponse): GameState => ({
  ...state,
  recentSessions: response.sessions,
  recentTotal: response.total,
  recentPage: 1,
  isLoading: false,
  isRecentPageLoading: false,
  sessionsLoaded: true,
});

export const completeSessionsPage = (state: GameState, page: number, response: AccountSessionsResponse): GameState => ({
  ...state,
  recentSessions: response.sessions,
  recentTotal: response.total,
  recentPage: page,
  isRecentPageLoading: false,
});

export const beginRecentPageLoad = (state: GameState): GameState => ({
  ...state,
  isRecentPageLoading: true,
  gameError: "",
});

export const failSessions = (state: GameState, error: string): GameState => ({
  ...state,
  recentSessions: [],
  recentTotal: 0,
  isLoading: false,
  isRecentPageLoading: false,
  sessionsLoaded: true,
  gameError: error,
});

export const beginStatsLoad = (state: GameState): GameState => ({ ...state, statsError: "" });

export const completeStats = (state: GameState, response: GameStatsResponse): GameState => ({
  ...state,
  stats: response,
  statsError: "",
});

export const failStats = (state: GameState, error: string): GameState => ({ ...state, statsError: error });

export const completeIgnore = (state: GameState, response: GameIgnoreResponse): GameState => ({
  ...state,
  isIgnored: response.ignored,
  ignoreError: "",
  isIgnoreChecking: false,
});

export const failIgnore = (state: GameState, error: string): GameState => ({
  ...state,
  isIgnored: false,
  ignoreError: error,
  isIgnoreChecking: false,
});

export const completeRating = (state: GameState, response: GameRatingResponse): GameState => ({
  ...state,
  rating: response.rating,
  ratingError: "",
  isRatingLoading: false,
});

export const failRating = (state: GameState, error: string): GameState => ({
  ...state,
  rating: null,
  ratingError: error,
  isRatingLoading: false,
});

export function buildIgnoreSubmission(state: GameState, isIgnored: boolean): GameMutationSubmission {
  if (state.requests === null) throw new Error("No game is loaded.");
  return {
    path: "/games/ignored",
    method: isIgnored ? "DELETE" : "POST",
    body: { gameId: state.requests.gameHash },
  };
}

export function buildRatingSubmission(state: GameState, value: number | null): GameMutationSubmission {
  if (state.requests === null) throw new Error("No game is loaded.");
  const body: Record<string, string | number> = { gameId: state.requests.gameHash };
  if (value !== null) body.rating = value;
  return { path: "/games/rating", method: value !== null ? "POST" : "DELETE", body };
}

export const beginIgnoreSave = (state: GameState): GameState => ({ ...state, isIgnoreSaving: true, ignoreError: "" });
export const endIgnoreSave = (state: GameState): GameState => ({ ...state, isIgnoreSaving: false });
export const beginRatingSave = (state: GameState): GameState => ({ ...state, isRatingSaving: true, ratingError: "" });
export const endRatingSave = (state: GameState): GameState => ({ ...state, isRatingSaving: false });
export const setSelectedYear = (state: GameState, year: number): GameState => ({ ...state, selectedYear: year });

// ---- Derived values -------------------------------------------------------

export function displayTitle(state: GameState): string {
  return state.gameDetails?.title ?? (state.gameDetailsError.length > 0 ? "Unknown game" : "Loading game...");
}

export function gameImage(state: GameState): string | null {
  return state.recentSessions.find((session) => session.capsuleUrl !== null)?.capsuleUrl ?? state.gameDetails?.capsuleUrl ?? null;
}

export function totalSeconds(state: GameState): number {
  return state.stats?.totalSeconds ?? 0;
}

export function lastPlayedLabel(state: GameState): string {
  const lastPlayed = state.stats?.lastPlayedAt;
  return lastPlayed ? formatDateLabel(lastPlayed) : "—";
}

function bucket(key: string, label: string, year: number, month: number, seconds: number): GameChartBucket {
  return { key, label, year, month, seconds, hours: seconds / 3600 };
}

/** One bucket per year from the first year played (taken from the stats) through the current year. */
export function yearlyStats(state: GameState): GameChartBucket[] {
  if (state.stats === null) return [];
  const currentYear = state.now.getFullYear();
  const played = state.stats.years.map((item) => item.year);
  const startYear = Math.min(currentYear, ...played);
  const endYear = Math.max(currentYear, ...played);
  const totals = new Map<number, number>();
  for (let year = startYear; year <= endYear; year++) totals.set(year, 0);
  for (const item of state.stats.years) {
    if (totals.has(item.year)) totals.set(item.year, item.seconds);
  }
  return [...totals].map(([year, seconds]) => bucket(String(year), String(year), year, -1, seconds));
}

export function availableYears(state: GameState): number[] {
  return yearlyStats(state).map((item) => item.year);
}

export function activeYear(state: GameState): number {
  const years = availableYears(state);
  if (years.includes(state.selectedYear)) return state.selectedYear;
  return years.length > 0 ? years[years.length - 1] : state.selectedYear;
}

export function monthlyStats(state: GameState): GameChartBucket[] {
  const year = activeYear(state);
  const totals = new Array<number>(12).fill(0);
  for (const item of state.stats?.months ?? []) {
    if (item.year === year && item.month >= 1 && item.month <= 12) totals[item.month - 1] = item.seconds;
  }
  return totals.map((seconds, month) => bucket(`${year}-${month}`, MonthShort[month], year, month, seconds));
}

export function recentTotalPages(state: GameState): number {
  return Math.max(1, Math.ceil(state.recentTotal / RecentPageSize));
}

export function ratingLabel(state: GameState): string {
  switch (state.rating) {
    case 1:
      return "Rough";
    case 2:
      return "Okay";
    case 3:
      return "Good";
    case 4:
      return "Great";
    case 5:
      return "Legendary";
    default:
      return "Not rated";
  }
}

export function isRatingDisabled(state: GameState): boolean {
  return state.isRatingSaving || state.isRatingLoading || state.requests === null;
}
