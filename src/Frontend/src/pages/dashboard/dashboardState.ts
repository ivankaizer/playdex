import { addDays, currentUtcWeek, getDurationSeconds, type DateOnly } from "@/lib/dates";
import { formatDuration, formatHours, formatTimeLabel, formatUtcDateInput } from "@/lib/format";
import { encodeGameId } from "@/lib/gameHash";
import type { ManualActiveSession } from "@/models/manual";
import type { AccountSession } from "@/models/sessions";
import type { AccountDayStat, AccountPlatformStat, AccountStatsPeriod } from "@/models/stats";

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
  /** Number of sessions logged today (from today's stats); falls back to `todaySessions.length`. */
  todaySessionCount?: number;
  /** Latest sessions across all days for the "Recent sessions" list. */
  recentSessions?: AccountSession[];
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
  todaySessionCount: number;
  recentSessions: AccountSession[];
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
  todaySessionCount: 0,
  recentSessions: [],
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
    todaySessionCount: snapshot.todaySessionCount ?? snapshot.todaySessions.length,
    recentSessions: snapshot.recentSessions ?? [],
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
    todaySessionCount: 0,
    recentSessions: [],
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

// ---------------------------------------------------------------------------
// Redesign: recent sessions, weekly/monthly/yearly activity and overview views.
// ---------------------------------------------------------------------------

export const recentSessionsRequest = "/sessions?limit=5&offset=0&sort=date&direction=desc";

const monthShortNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
const monthLongNames = [
  "January",
  "February",
  "March",
  "April",
  "May",
  "June",
  "July",
  "August",
  "September",
  "October",
  "November",
  "December",
];
const weekdayShortNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

/** "1h 55m" / "10m" / "0m" (the redesign's short duration). */
export function formatShortDuration(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(safe / 3600);
  const minutes = Math.floor((safe % 3600) / 60);
  return hours > 0 ? `${hours}h ${minutes}m` : `${minutes}m`;
}

/** Hours with one decimal and thousands separators, e.g. "1,520.4h". */
export function formatHoursGrouped(seconds: number): string {
  const hours = Math.max(0, seconds) / 3600;
  return `${hours.toLocaleString("en-US", { minimumFractionDigits: 0, maximumFractionDigits: 1 })}h`;
}

function pad2(value: number): string {
  return String(value).padStart(2, "0");
}

/** Local "HH:MM" (24h). */
export function formatClock(date: Date | string): string {
  const value = typeof date === "string" ? new Date(date) : date;
  return `${pad2(value.getHours())}:${pad2(value.getMinutes())}`;
}

function sameLocalDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate();
}

export interface DashboardRecentSessionView {
  sessionId: number;
  gameId: string;
  title: string;
  platform: string;
  platformKey: string;
  date: string;
  duration: string;
  imageUrl: string | null;
}

export function getRecentSessions(state: DashboardState, now: Date): DashboardRecentSessionView[] {
  return state.recentSessions.map((session) => {
    const started = new Date(session.startedAt);
    const date = sameLocalDay(started, now)
      ? `Today, ${formatClock(started)}`
      : `${monthShortNames[started.getMonth()]} ${started.getDate()}${started.getFullYear() === now.getFullYear() ? "" : `, ${started.getFullYear()}`}`;
    return {
      sessionId: session.sessionId,
      gameId: session.gameId,
      title: session.game,
      platform: session.platformName,
      platformKey: session.platformKey,
      date,
      duration: formatShortDuration(getDurationSeconds(session.startedAt, session.endedAt, now)),
      imageUrl: session.capsuleUrl,
    };
  });
}

export function todaySeconds(state: DashboardState): number {
  return sumSeconds(state.todayPlatformTotals);
}

/** Per-day totals (UTC days, like every other dashboard range) plus this week's stats. */
export interface DashboardActivity {
  weekStats: AccountStatsPeriod;
  days: Record<DateOnly, number>;
}

export interface DashboardActivityRequests {
  week: string;
  daily: string;
}

/** This week's stats and one daily breakdown from the earlier of Monday / the 1st through today (UTC days). */
export function dashboardActivityRequests(now: Date): DashboardActivityRequests {
  const today = formatUtcDateInput(now);
  const { monthStart } = utcPeriodStarts(now);
  const monday = currentUtcWeek(now).from;
  const first = monday < monthStart ? monday : monthStart;
  return { week: `/stats?from=${monday}&to=${today}`, daily: `/stats/daily?from=${first}&to=${today}` };
}

/** Indexes a daily breakdown by date. */
export function dailyTotals(days: readonly AccountDayStat[]): Record<DateOnly, number> {
  const totals: Record<DateOnly, number> = {};
  for (const day of days) totals[day.date] = day.totalSeconds;
  return totals;
}

export interface ActivityBar {
  key: string;
  value: number;
  label: string;
  title: string;
  highlight: boolean;
  faded: boolean;
}

export interface WeekView {
  bars: ActivityBar[];
  totalSeconds: number;
  rangeLabel: string;
  platforms: Array<DashboardPlatformView & { seconds: number }>;
}

function shortDate(date: DateOnly): string {
  const [, month, day] = date.split("-").map(Number);
  return `${monthShortNames[month - 1]} ${day}`;
}

export function getWeekView(activity: DashboardActivity | null, now: Date): WeekView {
  const today = formatUtcDateInput(now);
  const week = currentUtcWeek(now);
  const bars = weekdayShortNames.map((label, index) => {
    const date = addDays(week.from, index);
    const future = date > today;
    const seconds = future ? 0 : (activity?.days[date] ?? 0);
    return {
      key: date,
      value: seconds,
      label,
      title: `${label} · ${future ? "upcoming" : seconds > 0 ? formatShortDuration(seconds) : "no play"}`,
      highlight: date === today,
      faded: future,
    };
  });
  const weekStats = activity?.weekStats ?? null;
  return {
    bars,
    totalSeconds: weekStats?.totalSeconds ?? 0,
    rangeLabel: `${shortDate(week.from)} – ${shortDate(week.to)}`,
    platforms: weekStats
      ? createPlatforms(weekStats.platforms, weekStats.totalSeconds).map((item, index) => ({
          ...item,
          seconds: weekStats.platforms[index].totalSeconds,
        }))
      : [],
  };
}

export type OverviewPeriod = "month" | "year";

export interface OverviewKpi {
  label: string;
  value: string;
  sub?: string;
}

export interface OverviewView {
  title: string;
  totalSeconds: number;
  kpis: OverviewKpi[];
  activityLabel: string;
  activityNote: string;
  bars: ActivityBar[];
  platforms: Array<DashboardPlatformView & { seconds: number }>;
  topGames: Array<{ gameId: string; title: string; hours: string; pct: number }>;
}

export function getOverviewView(
  state: DashboardState,
  activity: DashboardActivity | null,
  period: OverviewPeriod,
  now: Date,
): OverviewView {
  const stats = period === "month" ? state.monthlyStats : state.yearlyStats;
  const total = stats?.totalSeconds ?? 0;
  const sessions = stats?.sessionCount ?? 0;
  const topGame = stats?.topGames[0] ?? null;
  const topShare = topGame && total > 0 ? Math.floor((topGame.totalSeconds / total) * 100 + 0.5) : null;
  const year = now.getUTCFullYear();
  const monthIndex = now.getUTCMonth();

  const bars = period === "month" ? monthBars(activity, now) : yearBars(stats, now);
  const played = bars.filter((bar) => !bar.faded && bar.value > 0);
  const peak = played.reduce<ActivityBar | null>((best, bar) => (best === null || bar.value > best.value ? bar : best), null);
  for (const bar of bars) bar.highlight = peak !== null && bar.key === peak.key;

  let activityNote = "";
  let hoursSub: string | undefined;
  if (period === "month") {
    const daysInMonth = bars.length;
    const dayOfMonth = now.getUTCDate();
    if (activity !== null) {
      hoursSub = `${played.length} of ${daysInMonth} days played`;
      activityNote = `${played.length} ${played.length === 1 ? "day" : "days"} played · ${dayOfMonth} of ${daysInMonth} days in`;
    }
  } else {
    hoursSub = "Since Jan 1";
    if (peak !== null) activityNote = `Peak ${monthLongNames[Number(peak.key) - 1]} · ${formatHoursGrouped(peak.value)}`;
  }

  const topSub =
    topGame === null || topShare === null
      ? undefined
      : period === "month"
        ? `${topShare}% of playtime`
        : `${formatHoursGrouped(topGame.totalSeconds)} · ${topShare}%`;

  const topMax = Math.max(stats?.topGames[0]?.totalSeconds ?? 0, 1);
  return {
    title: period === "month" ? `${monthLongNames[monthIndex]} highlights` : `${year} recap`,
    totalSeconds: total,
    kpis: [
      { label: "Hours played", value: formatHoursGrouped(total), sub: hoursSub },
      { label: "Sessions", value: sessions.toLocaleString("en-US"), sub: sessions > 0 ? `Avg. ${formatShortDuration(total / sessions)}` : undefined },
      { label: "Top title", value: stats?.topGameTitle ?? "—", sub: topSub },
    ],
    activityLabel: period === "month" ? "Daily activity" : "Monthly activity",
    activityNote,
    bars,
    platforms: stats
      ? createPlatforms(stats.platforms, stats.totalSeconds).map((item, index) => ({ ...item, seconds: stats.platforms[index].totalSeconds }))
      : [],
    topGames: (stats?.topGames ?? []).map((game) => ({
      gameId: game.gameId,
      title: game.title,
      hours: formatHoursGrouped(game.totalSeconds),
      pct: (game.totalSeconds / topMax) * 100,
    })),
  };
}

function monthBars(activity: DashboardActivity | null, now: Date): ActivityBar[] {
  const today = formatUtcDateInput(now);
  const { monthStart } = utcPeriodStarts(now);
  const year = now.getUTCFullYear();
  const month = now.getUTCMonth();
  const daysInMonth = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
  return Array.from({ length: daysInMonth }, (_, index) => {
    const day = index + 1;
    const date = addDays(monthStart, index);
    const future = date > today;
    const seconds = future ? 0 : (activity?.days[date] ?? 0);
    return {
      key: date,
      value: seconds,
      label: day % 5 === 1 || date === today ? String(day) : "",
      title: `${monthShortNames[month]} ${day} · ${future ? "upcoming" : seconds > 0 ? formatShortDuration(seconds) : "no play"}`,
      highlight: false,
      faded: future,
    };
  });
}

function yearBars(stats: AccountStatsPeriod | null, now: Date): ActivityBar[] {
  const year = now.getUTCFullYear();
  const currentMonth = now.getUTCMonth() + 1;
  return monthShortNames.map((label, index) => {
    const month = index + 1;
    const future = month > currentMonth;
    const seconds = future
      ? 0
      : (stats?.months.filter((item) => item.year === year && item.month === month).reduce((sum, item) => sum + item.totalSeconds, 0) ?? 0);
    return {
      key: String(month),
      value: seconds,
      label,
      title: `${label} · ${future ? "upcoming" : formatHoursGrouped(seconds)}`,
      highlight: false,
      faded: future,
    };
  });
}
