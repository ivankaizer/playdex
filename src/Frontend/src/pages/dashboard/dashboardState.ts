import { getDurationSeconds } from "@/lib/dates";
import { formatDuration, formatHours, formatTimeLabel, formatUtcDateInput } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import type { ManualActiveSession } from "@/models/manual";
import type { AccountSession } from "@/models/sessions";
import type { AccountPlatformStat, AccountStatsPeriod } from "@/models/stats";

const todaySessionsPageSize = 3;

export interface DashboardRequests {
  todaySessions: string;
  todayStats: string;
  monthlyStats: string;
  yearlyStats: string;
  currentSession: string;
  activeManualSession: string;
}

export function dashboardRequests(now: Date): DashboardRequests {
  const today = formatUtcDateInput(now);
  const { monthStart, yearStart } = utcPeriodStarts(now);
  return {
    todaySessions: `/sessions?limit=10&offset=0&from=${today}&to=${today}&sort=date&direction=desc`,
    todayStats: `/stats?from=${today}&to=${today}`,
    monthlyStats: `/stats?from=${monthStart}&to=${today}`,
    yearlyStats: `/stats?from=${yearStart}&to=${today}`,
    currentSession: "/sessions/current",
    activeManualSession: "/manual/sessions/current",
  };
}

export type RecapRange = "today" | "month" | "year";

export function recapPath(range: RecapRange, now: Date): string {
  const today = formatUtcDateInput(now);
  const { monthStart, yearStart } = utcPeriodStarts(now);
  const from = range === "today" ? today : range === "month" ? monthStart : yearStart;
  return `/recap?from=${from}&to=${today}`;
}

function utcPeriodStarts(now: Date): { monthStart: string; yearStart: string } {
  const year = String(now.getUTCFullYear()).padStart(4, "0");
  const month = String(now.getUTCMonth() + 1).padStart(2, "0");
  return { monthStart: `${year}-${month}-01`, yearStart: `${year}-01-01` };
}

/**
 * Waits for every request; on the first failure cancels the siblings and rethrows
 * that failure without waiting for the remaining requests.
 */
export async function waitAllOrCancel<T extends readonly unknown[]>(
  requests: { [K in keyof T]: Promise<T[K]> },
  siblingCancellation: AbortController,
): Promise<T> {
  try {
    return (await Promise.all(requests)) as unknown as T;
  } catch (error) {
    siblingCancellation.abort();
    throw error;
  }
}

export interface DashboardSnapshot {
  todaySessions: AccountSession[];
  todayPlatformTotals: AccountPlatformStat[];
  currentSession: AccountSession | null;
  activeManualSession: ManualActiveSession | null;
  monthlyStats: AccountStatsPeriod;
  yearlyStats: AccountStatsPeriod;
}

export interface DashboardSessionCard {
  sessionId: number;
  gameId: string;
  gamePath: string;
  title: string;
  duration: string;
  platform: string;
  imageUrl: string | null;
}

export interface DashboardPlatformView {
  key: string;
  label: string;
  value: number;
  hours: string;
}

export interface DashboardTopGameView {
  gameId: string;
  title: string;
  hours: string;
}

export interface DashboardStatsView {
  hours: string;
  sessions: string;
  topTitle: string;
  platforms: DashboardPlatformView[];
  topGames: DashboardTopGameView[];
}

export interface DashboardCurrentSessionView {
  gameId: string;
  gamePath: string;
  title: string;
  platform: string;
  startedAt: string;
  elapsed: string;
  canStop: boolean;
}

export interface DashboardState {
  todaySessions: AccountSession[];
  todaySessionCards: DashboardSessionCard[];
  todayPlatformTotals: AccountPlatformStat[];
  currentSession: AccountSession | null;
  activeManualSession: ManualActiveSession | null;
  monthlyStats: AccountStatsPeriod | null;
  yearlyStats: AccountStatsPeriod | null;
  isDashboardLoading: boolean;
  dashboardError: string;
  todaySessionsPage: number;
}

export const initialDashboardState: DashboardState = {
  todaySessions: [],
  todaySessionCards: [],
  todayPlatformTotals: [],
  currentSession: null,
  activeManualSession: null,
  monthlyStats: null,
  yearlyStats: null,
  isDashboardLoading: false,
  dashboardError: "",
  todaySessionsPage: 1,
};

export function beginRefresh(state: DashboardState, silent: boolean): DashboardState {
  return { ...state, isDashboardLoading: silent ? state.isDashboardLoading : true, dashboardError: "" };
}

export function completeRefresh(state: DashboardState, snapshot: DashboardSnapshot, now: Date): DashboardState {
  return {
    ...state,
    todaySessions: snapshot.todaySessions,
    todaySessionCards: createTodaySessionCards(snapshot.todaySessions, now),
    todayPlatformTotals: snapshot.todayPlatformTotals,
    currentSession: snapshot.currentSession,
    activeManualSession: snapshot.activeManualSession,
    monthlyStats: snapshot.monthlyStats,
    yearlyStats: snapshot.yearlyStats,
    isDashboardLoading: false,
    todaySessionsPage: 1,
  };
}

export function failRefresh(state: DashboardState, error: string): DashboardState {
  return {
    ...state,
    todaySessions: [],
    todaySessionCards: [],
    todayPlatformTotals: [],
    currentSession: null,
    activeManualSession: null,
    monthlyStats: null,
    yearlyStats: null,
    isDashboardLoading: false,
    dashboardError: error,
    todaySessionsPage: 1,
  };
}

export function todayHours(state: DashboardState): string {
  return formatHours(sumSeconds(state.todayPlatformTotals), 1);
}

export function todaySessionsTotalPages(state: DashboardState): number {
  return Math.max(1, Math.ceil(state.todaySessionCards.length / todaySessionsPageSize));
}

export function todaySessionPageItems(state: DashboardState): DashboardSessionCard[] {
  const start = (state.todaySessionsPage - 1) * todaySessionsPageSize;
  return state.todaySessionCards.slice(start, start + todaySessionsPageSize);
}

export function setTodaySessionsPage(state: DashboardState, page: number): DashboardState {
  const clamped = Math.min(Math.max(page, 1), todaySessionsTotalPages(state));
  return { ...state, todaySessionsPage: clamped };
}

export function getTodayPlatforms(state: DashboardState): DashboardPlatformView[] {
  return createPlatforms(state.todayPlatformTotals, sumSeconds(state.todayPlatformTotals));
}

export function getCurrentSession(state: DashboardState, now: Date): DashboardCurrentSessionView | null {
  const manual = state.activeManualSession;
  if (manual) {
    return {
      gameId: manual.gameId,
      gamePath: `/games/${encodeGameId(manual.gameId)}`,
      title: manual.title,
      platform: manual.platformName,
      startedAt: formatTimeLabel(manual.startedAt),
      elapsed: formatDuration(getDurationSeconds(manual.startedAt, null, now), "full"),
      canStop: true,
    };
  }

  const current = state.currentSession;
  if (!current) return null;

  return {
    gameId: current.gameId,
    gamePath: `/games/${encodeGameId(current.gameId)}`,
    title: current.game,
    platform: current.platformName,
    startedAt: formatTimeLabel(current.startedAt),
    elapsed: formatDuration(getDurationSeconds(current.startedAt, current.endedAt, now), "full"),
    canStop: false,
  };
}

export function getStatsView(stats: AccountStatsPeriod | null): DashboardStatsView {
  if (!stats) return { hours: "0h", sessions: "0", topTitle: "—", platforms: [], topGames: [] };

  return {
    hours: formatHours(stats.totalSeconds, 1),
    sessions: String(stats.sessionCount),
    topTitle: stats.topGameTitle ?? "—",
    platforms: createPlatforms(stats.platforms, stats.totalSeconds),
    topGames: stats.topGames.map((game) => ({
      gameId: game.gameId,
      title: game.title,
      hours: formatHours(game.totalSeconds, 1),
    })),
  };
}

function createTodaySessionCards(sessions: readonly AccountSession[], now: Date): DashboardSessionCard[] {
  return sessions.map((session) => ({
    sessionId: session.sessionId,
    gameId: session.gameId,
    gamePath: `/games/${encodeGameId(session.gameId)}`,
    title: session.game,
    duration: formatDuration(getDurationSeconds(session.startedAt, session.endedAt, now), "full"),
    platform: session.platformName,
    imageUrl: session.capsuleUrl,
  }));
}

function createPlatforms(platforms: readonly AccountPlatformStat[], totalSeconds: number): DashboardPlatformView[] {
  const safeTotal = Math.max(totalSeconds, 1);
  return platforms.map((item) => ({
    key: item.platformKey,
    label: item.platformName,
    value: Math.floor((item.totalSeconds / safeTotal) * 100 + 0.5),
    hours: formatHours(item.totalSeconds, 1),
  }));
}

function sumSeconds(platforms: readonly AccountPlatformStat[]): number {
  return platforms.reduce((sum, item) => sum + item.totalSeconds, 0);
}
