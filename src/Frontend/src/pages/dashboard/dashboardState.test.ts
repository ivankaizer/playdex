import { describe, expect, it } from "vitest";
import type { AccountSession } from "@/models/sessions";
import type { AccountStatsPeriod } from "@/models/stats";
import {
  beginRefresh,
  completeRefresh,
  dashboardRequests,
  failRefresh,
  getCurrentSession,
  getStatsView,
  getTodayPlatforms,
  initialDashboardState,
  recapPath,
  setTodaySessionsPage,
  todayHours,
  todaySessionPageItems,
  todaySessionsTotalPages,
  waitAllOrCancel,
  type DashboardSnapshot,
} from "./dashboardState";

const now = new Date("2026-07-13T12:00:00Z");
const offset = (seconds: number) => new Date(now.getTime() + seconds * 1000).toISOString();

function snapshot(): DashboardSnapshot {
  const completed: AccountSession = {
    sessionId: 1,
    startedAt: offset(-7200),
    endedAt: offset(-3539),
    gameId: "42",
    platformKey: "steam",
    platformName: "Steam",
    game: "Elden Ring",
    capsuleUrl: "https://images.test/elden.jpg",
    ignored: false,
    mappedPlatform: "psn",
    mappedSource: "local",
  };
  const current: AccountSession = {
    sessionId: 2,
    startedAt: offset(-3661),
    endedAt: null,
    gameId: "43",
    platformKey: "manual",
    platformName: "Manual",
    game: "Hades II",
    capsuleUrl: null,
    ignored: false,
    mappedPlatform: null,
    mappedSource: null,
  };
  const todayStats: AccountStatsPeriod = {
    totalSeconds: 3600,
    sessionCount: 1,
    topGameTitle: "Elden Ring",
    platforms: [
      { platformKey: "steam", platformName: "Steam", totalSeconds: 450, source: null },
      { platformKey: "psn", platformName: "PlayStation Network", totalSeconds: 3150, source: null },
    ],
    topGames: [{ gameId: "42", title: "Elden Ring", totalSeconds: 3600 }],
    months: [],
  };
  const monthly: AccountStatsPeriod = {
    totalSeconds: 7200,
    sessionCount: 2,
    topGameTitle: "Elden Ring",
    platforms: [{ platformKey: "steam", platformName: "Steam", totalSeconds: 7200, source: null }],
    topGames: [{ gameId: "42", title: "Elden Ring", totalSeconds: 7200 }],
    months: [],
  };
  const yearly: AccountStatsPeriod = {
    totalSeconds: 10800,
    sessionCount: 3,
    topGameTitle: "Hades II",
    platforms: [{ platformKey: "manual", platformName: "Manual", totalSeconds: 10800, source: null }],
    topGames: [{ gameId: "43", title: "Hades II", totalSeconds: 10800 }],
    months: [],
  };

  return {
    todaySessions: [completed],
    todayPlatformTotals: todayStats.platforms,
    currentSession: current,
    activeManualSession: {
      sessionId: 2,
      gameId: "43",
      title: "Hades II",
      platformKey: "pc",
      platformName: "PC",
      startedAt: offset(-3661),
      capsuleUrl: null,
    },
    monthlyStats: monthly,
    yearlyStats: yearly,
  };
}

describe("dashboardRequests", () => {
  it("includes the manual live session alongside dashboard data", () => {
    expect(Object.values(dashboardRequests(now))).toEqual([
      "/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-13&sort=date&direction=desc",
      "/stats?from=2026-07-13&to=2026-07-13",
      "/stats?from=2026-07-01&to=2026-07-13",
      "/stats?from=2026-01-01&to=2026-07-13",
      "/sessions/current",
      "/manual/sessions/current",
    ]);
  });

  it("builds recap paths from UTC dates", () => {
    expect(recapPath("today", now)).toBe("/recap?from=2026-07-13&to=2026-07-13");
    expect(recapPath("month", now)).toBe("/recap?from=2026-07-01&to=2026-07-13");
    expect(recapPath("year", now)).toBe("/recap?from=2026-01-01&to=2026-07-13");
  });
});

describe("dashboard refresh state", () => {
  it("is all-or-nothing and a silent refresh does not show the skeleton", () => {
    const data = snapshot();
    let state = beginRefresh(initialDashboardState, false);
    expect(state.isDashboardLoading).toBe(true);
    state = completeRefresh(state, data, now);

    expect(state.isDashboardLoading).toBe(false);
    expect(state.todaySessions).toHaveLength(1);
    expect(state.currentSession).toBe(data.currentSession);
    expect(state.activeManualSession).toBe(data.activeManualSession);
    expect(state.monthlyStats).toBe(data.monthlyStats);
    expect(state.yearlyStats).toBe(data.yearlyStats);

    state = beginRefresh(state, true);
    expect(state.isDashboardLoading).toBe(false);
    state = failRefresh(state, "Dashboard unavailable.");

    expect(state.todaySessions).toEqual([]);
    expect(state.todayPlatformTotals).toEqual([]);
    expect(state.currentSession).toBeNull();
    expect(state.activeManualSession).toBeNull();
    expect(state.monthlyStats).toBeNull();
    expect(state.yearlyStats).toBeNull();
    expect(state.dashboardError).toBe("Dashboard unavailable.");
    expect(beginRefresh(state, true).dashboardError).toBe("");
  });

  it("derives cards, totals and current session with matching formatting and rounding", () => {
    const state = completeRefresh(initialDashboardState, snapshot(), now);

    expect(state.todaySessionCards).toHaveLength(1);
    const card = state.todaySessionCards[0];
    expect(card.title).toBe("Elden Ring");
    expect(card.duration).toBe("1h 1m");
    expect(card.platform).toBe("Steam");
    expect(card.gamePath).toBe("/games/3H4h3");

    expect(todayHours(state)).toBe("1h");
    expect(getTodayPlatforms(state)[0].value).toBe(13);

    const current = getCurrentSession(state, now);
    expect(current?.platform).toBe("PC");
    expect(current?.elapsed).toBe("1h 1m");
    expect(current?.canStop).toBe(true);

    const monthly = getStatsView(state.monthlyStats);
    expect(monthly.hours).toBe("2h");
    expect(monthly.sessions).toBe("2");
    expect(monthly.topTitle).toBe("Elden Ring");
  });

  it("shows an empty stats view without data", () => {
    expect(getStatsView(null)).toEqual({ hours: "0h", sessions: "0", topTitle: "—", platforms: [], topGames: [] });
  });

  it("keeps a synced current session visible without the manual stop action", () => {
    const state = completeRefresh(initialDashboardState, { ...snapshot(), activeManualSession: null }, now);
    const current = getCurrentSession(state, now);
    expect(current?.title).toBe("Hades II");
    expect(current?.canStop).toBe(false);
  });

  it("freezes today session cards while the current elapsed time advances", () => {
    const data = snapshot();
    const open = { ...data.todaySessions[0], startedAt: offset(-600), endedAt: null };
    const state = completeRefresh(initialDashboardState, { ...data, todaySessions: [open] }, now);

    expect(state.todaySessionCards[0].duration).toBe("10 min");
    expect(getCurrentSession(state, now)?.elapsed).not.toBe(
      getCurrentSession(state, new Date(now.getTime() + 5 * 60_000))?.elapsed,
    );
  });

  it("paginates today session cards by three with a clamped page", () => {
    const sessions: AccountSession[] = Array.from({ length: 8 }, (_, i) => ({
      sessionId: i + 1,
      startedAt: offset(-(i + 1) * 3600),
      endedAt: offset(-(i + 1) * 3600 + 3600),
      gameId: "42",
      platformKey: "steam",
      platformName: "Steam",
      game: `Game ${i + 1}`,
      capsuleUrl: null,
      ignored: false,
      mappedPlatform: null,
      mappedSource: null,
    }));
    let state = completeRefresh(initialDashboardState, { ...snapshot(), todaySessions: sessions }, now);
    const ids = () => todaySessionPageItems(state).map((item) => item.sessionId);

    expect(state.todaySessionCards).toHaveLength(8);
    expect(todaySessionsTotalPages(state)).toBe(3);
    expect(ids()).toEqual([1, 2, 3]);

    state = setTodaySessionsPage(state, 2);
    expect(ids()).toEqual([4, 5, 6]);
    state = setTodaySessionsPage(state, 3);
    expect(ids()).toEqual([7, 8]);
    state = setTodaySessionsPage(state, 99);
    expect(state.todaySessionsPage).toBe(3);
    state = setTodaySessionsPage(state, 0);
    expect(state.todaySessionsPage).toBe(1);
  });

  it("uses platform names as labels while preserving distinct keys", () => {
    const view = getStatsView({
      totalSeconds: 7200,
      sessionCount: 2,
      topGameTitle: null,
      platforms: [
        { platformKey: "psn", platformName: "PlayStation Network", totalSeconds: 3600, source: null },
        { platformKey: "ps5", platformName: "PlayStation 5", totalSeconds: 3600, source: null },
      ],
      topGames: [],
      months: [],
    });
    expect(view.platforms.map((item) => item.label)).toEqual(["PlayStation Network", "PlayStation 5"]);
    expect(view.platforms.map((item) => item.key)).toEqual(["psn", "ps5"]);
    expect(view.topTitle).toBe("—");
  });
});

describe("waitAllOrCancel", () => {
  it("reports the first failure without waiting for an unresolved sibling", async () => {
    const sibling = new AbortController();
    const failure = new Error("first request failed");
    const unresolved = new Promise<number>(() => {});

    await expect(waitAllOrCancel<[number, number]>([unresolved, Promise.reject(failure)], sibling)).rejects.toBe(failure);
    expect(sibling.signal.aborted).toBe(true);
  });

  it("resolves all values in order", async () => {
    const sibling = new AbortController();
    await expect(waitAllOrCancel<[number, string]>([Promise.resolve(1), Promise.resolve("a")], sibling)).resolves.toEqual([1, "a"]);
    expect(sibling.signal.aborted).toBe(false);
  });
});
