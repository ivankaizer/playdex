import { describe, expect, it } from "vitest";
import type { AccountSession } from "@/models/sessions";
import type { AccountStatsPeriod } from "@/models/stats";
import {
  beginRefresh,
  completeRefresh,
  dailySessionCounts,
  dailyTotals,
  dashboardActivityRequests,
  dashboardRequests,
  formatShortDuration,
  getOverviewView,
  getRecentSessions,
  getTodayView,
  getWeekView,
  greetingFor,
  failRefresh,
  getCurrentSession,
  getStatsView,
  getTodayPlatforms,
  initialDashboardState,
  recapPath,
  setTodaySessionsPage,
  todayHours,
  todaySeconds,
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
  };
  const todayStats: AccountStatsPeriod = {
    totalSeconds: 3600,
    sessionCount: 1,
    topGameTitle: "Elden Ring",
    platforms: [
      { platformKey: "steam", platformName: "Steam", totalSeconds: 450 },
      { platformKey: "psn", platformName: "PlayStation Network", totalSeconds: 3150 },
    ],
    topGames: [{ gameId: "42", title: "Elden Ring", totalSeconds: 3600 }],
    months: [],
  };
  const monthly: AccountStatsPeriod = {
    totalSeconds: 7200,
    sessionCount: 2,
    topGameTitle: "Elden Ring",
    platforms: [{ platformKey: "steam", platformName: "Steam", totalSeconds: 7200 }],
    topGames: [{ gameId: "42", title: "Elden Ring", totalSeconds: 7200 }],
    months: [],
  };
  const yearly: AccountStatsPeriod = {
    totalSeconds: 10800,
    sessionCount: 3,
    topGameTitle: "Hades II",
    platforms: [{ platformKey: "manual", platformName: "Manual", totalSeconds: 10800 }],
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
        { platformKey: "psn", platformName: "PlayStation Network", totalSeconds: 3600 },
        { platformKey: "ps5", platformName: "PlayStation 5", totalSeconds: 3600 },
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

describe("dashboard redesign views", () => {
  it("requests one daily breakdown from the earlier of Monday and the 1st through today", () => {
    const requests = dashboardActivityRequests(new Date("2026-10-04T12:00:00Z"));
    expect(requests.daily).toBe("/stats/daily?from=2026-09-28&to=2026-10-04");

    const midMonth = dashboardActivityRequests(new Date("2026-10-20T12:00:00Z"));
    expect(midMonth.daily).toBe("/stats/daily?from=2026-10-01&to=2026-10-20");
  });

  it("indexes daily totals by date", () => {
    expect(
      dailyTotals([
        { date: "2026-10-01", totalSeconds: 600, sessionCount: 1, platforms: [] },
        { date: "2026-10-02", totalSeconds: 0, sessionCount: 0, platforms: [] },
      ]),
    ).toEqual({ "2026-10-01": 600, "2026-10-02": 0 });
  });

  it("builds week bars with today highlighted and future days faded", () => {
    const week = getWeekView({ days: { "2026-07-13": 600 } }, now);
    expect(week.bars.map((bar) => bar.label)).toEqual(["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"]);
    expect(week.bars[0]).toMatchObject({ value: 600, highlight: true, faded: false, title: "Mon · 10m" });
    expect(week.bars[1]).toMatchObject({ faded: true, title: "Tue · upcoming" });
    expect(week.rangeLabel).toBe("Jul 13 – Jul 19");
    // The week total is the time shown by the bars (sessions split at midnight), not the start-day stats.
    expect(week.totalSeconds).toBe(600);
  });

  it("splits the week's platforms from the same days as the bars", () => {
    const week = getWeekView(
      {
        days: { "2026-07-13": 5400 },
        dayPlatforms: {
          "2026-07-13": [
            { platformKey: "psn", platformName: "PlayStation", totalSeconds: 1800 },
            { platformKey: "steam", platformName: "Steam", totalSeconds: 3600 },
          ],
          // Outside this week (and a future day of it): ignored.
          "2026-07-12": [{ platformKey: "xbox", platformName: "Xbox", totalSeconds: 999 }],
          "2026-07-14": [{ platformKey: "xbox", platformName: "Xbox", totalSeconds: 999 }],
        },
      },
      now,
    );
    expect(week.platforms.map((item) => [item.label, item.seconds])).toEqual([
      ["Steam", 3600],
      ["PlayStation", 1800],
    ]);
  });

  it("takes today from the daily breakdown so a running or overnight session counts", () => {
    const state = completeRefresh(initialDashboardState, snapshot(), now);
    const activity = {
      days: { "2026-07-13": 5400 },
      sessionCounts: { "2026-07-13": 2 },
    };
    expect(getTodayView(state, activity, now)).toEqual({ seconds: 5400, sessions: 2 });
  });

  it("falls back to today's stats until the daily breakdown loads", () => {
    const state = completeRefresh(initialDashboardState, snapshot(), now);
    expect(getTodayView(state, null, now)).toEqual({ seconds: todaySeconds(state), sessions: state.todaySessionCount });
  });

  it("indexes daily session counts by date", () => {
    expect(dailySessionCounts([{ date: "2026-07-13", totalSeconds: 60, sessionCount: 3, platforms: [] }])).toEqual({ "2026-07-13": 3 });
  });

  it("derives month and year overview KPIs, bars and peak from real totals", () => {
    const state = completeRefresh(initialDashboardState, snapshot(), now);
    const month = getOverviewView(state, { days: { "2026-07-02": 3600, "2026-07-13": 7200 } }, "month", now);
    expect(month.title).toBe("July highlights");
    expect(month.bars).toHaveLength(31);
    expect(month.bars.filter((bar) => bar.highlight).map((bar) => bar.key)).toEqual(["2026-07-13"]);
    expect(month.kpis).toEqual([
      { label: "Hours played", value: "2h", sub: "2 of 31 days played" },
      { label: "Sessions", value: "2", sub: "Avg. 1h 0m" },
      { label: "Top title", value: "Elden Ring", sub: "100% of playtime" },
    ]);
    expect(month.activityNote).toBe("2 days played · 13 of 31 days in");

    const yearly = { ...snapshot().yearlyStats, months: [{ year: 2026, month: 3, totalSeconds: 10800 }] };
    const year = getOverviewView({ ...state, yearlyStats: yearly }, null, "year", now);
    expect(year.title).toBe("2026 recap");
    expect(year.bars.map((bar) => bar.faded)).toEqual([false, false, false, false, false, false, false, true, true, true, true, true]);
    expect(year.activityNote).toBe("Peak March · 3h");
    expect(year.kpis[2].sub).toBe("3h · 100%");
  });

  it("formats recent sessions with today and short dates", () => {
    const data = snapshot();
    const older = { ...data.todaySessions[0], sessionId: 9, startedAt: "2026-06-30T10:00:00Z", endedAt: "2026-06-30T11:55:00Z" };
    const state = completeRefresh(initialDashboardState, { ...data, recentSessions: [older] }, now);
    expect(getRecentSessions(state, now)[0]).toMatchObject({ date: "Jun 30", duration: "1h 55m" });
    expect(formatShortDuration(600)).toBe("10m");
  });

  it("shows <1% and <0.1h for slivers and plural-aware notes", () => {
    const week = getWeekView(
      {
        days: { "2026-07-13": 36060 },
        dayPlatforms: {
          "2026-07-13": [
            { platformKey: "steam", platformName: "Steam", totalSeconds: 36000 },
            { platformKey: "custom", platformName: "Custom", totalSeconds: 60 },
          ],
        },
      },
      now,
    );
    expect(week.platforms.map((item) => [item.share, item.hours])).toEqual([
      ["100%", "10h"],
      ["<1%", "<0.1h"],
    ]);

    const state = completeRefresh(initialDashboardState, snapshot(), now);
    const oneDay = getOverviewView(state, { days: { "2026-07-13": 600 } }, "month", now);
    expect(oneDay.activityNote).toBe("1 day played · 13 of 31 days in");
  });

  it("renders an empty account without NaN and with empty-state notes", () => {
    const empty: AccountStatsPeriod = { totalSeconds: 0, sessionCount: 0, topGameTitle: null, platforms: [], topGames: [], months: [] };
    const state = completeRefresh(initialDashboardState, { ...snapshot(), monthlyStats: empty, yearlyStats: empty, recentSessions: [] }, now);
    const month = getOverviewView(state, { days: {} }, "month", now);
    expect(month.kpis).toEqual([
      { label: "Hours played", value: "0h", sub: "0 of 31 days played" },
      { label: "Sessions", value: "0", sub: "—" },
      { label: "Top title", value: "—", sub: undefined },
    ]);
    expect(month.activityNote).toBe("No play yet");
    expect(month.fewGamesNote).toBe("No games yet this month. Your ranking fills in as you play.");
    const year = getOverviewView(state, null, "year", now);
    expect(year.activityNote).toBe("No play yet");
    expect(JSON.stringify([month, year])).not.toContain("NaN");
    expect(getWeekView(null, now).platforms).toEqual([]);
  });

  it("notes a short top-games list and scales bars to the largest game", () => {
    const state = completeRefresh(initialDashboardState, snapshot(), now);
    const two: AccountStatsPeriod = {
      totalSeconds: 3_000_000,
      sessionCount: 128_403,
      topGameTitle: "B",
      platforms: [],
      topGames: [
        { gameId: "1", title: "A", totalSeconds: 1000 },
        { gameId: "2", title: "B", totalSeconds: 2000 },
      ],
      months: [],
    };
    const view = getOverviewView({ ...state, yearlyStats: two }, null, "year", now);
    expect(view.topGames.map((game) => game.pct)).toEqual([50, 100]);
    expect(view.fewGamesNote).toBe("Only 2 games so far this year. Your ranking fills in as you play.");
    expect(view.kpis[0].value).toBe("833.3h");
    expect(view.kpis[1].value).toBe("128,403");
    expect(view.kpis[2].sub).toBe("0.3h · <1%");
    expect(getOverviewView(state, null, "month", now).fewGamesNote).toBe(
      "Only one game so far this month. Your ranking fills in as you play.",
    );
  });

  it("labels yesterday's sessions and a week that spans New Year", () => {
    const data = snapshot();
    const yesterday = new Date(now.getFullYear(), now.getMonth(), now.getDate() - 1, 23, 58);
    const late = { ...data.todaySessions[0], sessionId: 10, startedAt: yesterday.toISOString(), endedAt: now.toISOString() };
    const state = completeRefresh(initialDashboardState, { ...data, recentSessions: [late] }, now);
    expect(getRecentSessions(state, now)[0].date).toBe("Yesterday, 23:58");
    expect(getWeekView(null, new Date("2026-01-01T12:00:00Z")).rangeLabel).toBe("Dec 29, 2025 – Jan 4, 2026");
  });

  it("greets by the local hour", () => {
    expect(greetingFor(new Date(2026, 9, 10, 4))).toBe("Good evening");
    expect(greetingFor(new Date(2026, 9, 10, 9))).toBe("Good morning");
    expect(greetingFor(new Date(2026, 9, 10, 13))).toBe("Good afternoon");
    expect(greetingFor(new Date(2026, 9, 10, 19))).toBe("Good evening");
  });
});
