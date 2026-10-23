import { clientTimeZone } from "@/lib/dates";
import type { RecapResponse } from "./recapModels";
import {
  applyLocationQuery,
  beginLoad,
  beginYearsLoad,
  buildCustomNavigation,
  completeLoad,
  completeYears,
  failLoad,
  failYears,
  initialRecapState,
  recapTabs,
  runInitialCustomFlow,
  selectTab,
  setCustomFrom,
  setCustomTo,
  takeDefaultRequest,
  type RecapState,
} from "./recapState";
import { buildWeekdayChart, formatAverage, formatNumber, formatRecapMonthLabel, roundPercent } from "./recapView";

function deferred() {
  let resolve!: () => void;
  const promise = new Promise<void>((done) => {
    resolve = done;
  });
  return { promise, resolve };
}

const flush = () => new Promise((resolve) => setTimeout(resolve, 0));
const tz = `tz=${encodeURIComponent(clientTimeZone())}`;

/** A tiny mutable holder so async flows can read the latest state, like the page does. */
function store(initial: RecapState = initialRecapState) {
  let state = initial;
  return {
    get: () => state,
    apply: (transition: (current: RecapState) => RecapState) => {
      state = transition(state);
    },
    step: <R,>(transition: (current: RecapState) => readonly [RecapState, R]): R => {
      const [next, result] = transition(state);
      state = next;
      return result;
    },
  };
}

function recap(totalSessions: number): RecapResponse {
  return {
    range: { from: null, to: null },
    totalSessions,
    totalSeconds: 3600,
    averageSessionSeconds: 3600,
    medianSessionSeconds: 3600,
    longestSessionSeconds: 3600,
    shortestSessionSeconds: 3600,
    averageSessionsPerDay: 1,
    averageSessionsPerWeek: 1,
    averageSessionsPerMonth: 1,
    mostActiveDay: null,
    mostActiveMonth: null,
    longestStreakDays: 1,
    daysPlayed: 1,
    daysNotPlayed: 0,
    sessionsByWeekday: [],
    sessionsByHour: [],
    peakHour: null,
    weekendSeconds: 0,
    weekdaySeconds: 3600,
    uniqueGames: 1,
    topGameBySessions: null,
    topGameByTime: null,
    topGamesByTime: [],
    topGameByAverageSession: null,
    gameWithLongestSession: null,
    newGamesTried: 1,
    abandonedGames: 0,
    returnRateGames: 0,
    topThreeGamesSharePercent: 100,
    mostBingedGame: null,
    platformTotals: [],
    mostUsedPlatform: null,
    gamesTable: [],
  };
}

describe("recap formatting", () => {
  it.each([
    [2.55, 1, "2.5"],
    [2.25, 1, "2.3"],
    [-2.55, 1, "-2.5"],
    [-2.25, 1, "-2.3"],
    [2.0, 1, "2"],
    [Number.NaN, 1, "0"],
    [Number.POSITIVE_INFINITY, 1, "0"],
    [Number.NEGATIVE_INFINITY, 1, "0"],
    [0.1, 100, "0.1"],
    [1e21, 1, "1e+21"],
  ])("formats average %d with %d decimals as %s", (value, decimals, expected) => {
    expect(formatAverage(value, decimals)).toBe(expected);
  });

  it("formats numbers like the .NET 0.### custom format", () => {
    expect(formatNumber(8)).toBe("8");
    expect(formatNumber(0.2 + 0.4)).toBe("0.6");
    expect(formatNumber(100 / 3)).toBe("33.3333333333333");
    expect(formatNumber(1e-7)).toBe("0.0000001");
    expect(formatNumber(1e-16)).toBe("0");
  });

  it("rounds percents half up", () => {
    expect(roundPercent(49.5)).toBe(50);
    expect(roundPercent(49.49)).toBe(49);
    expect(roundPercent(0)).toBe(0);
  });

  it("formats month keys", () => {
    expect(formatRecapMonthLabel("2026-07")).toBe("Jul 2026");
    expect(formatRecapMonthLabel("2026-13")).toBe("2026-13");
    expect(formatRecapMonthLabel("garbage")).toBe("garbage");
    expect(formatRecapMonthLabel("0000-01")).toBe("0000-01");
  });

  it("builds the weekday chart coordinates", () => {
    const chart = buildWeekdayChart([
      { weekday: 1, sessionCount: 1 },
      { weekday: 2, sessionCount: 4 },
      { weekday: 3, sessionCount: 2 },
    ]);

    expect(chart.width).toBe(240);
    expect(chart.height).toBe(80);
    expect(chart.linePoints).toBe("8,56 120,8 232,40");
    expect(chart.areaPath).toBe("M 8,72 L 8,56 120,8 232,40 L 232,72 Z");
    expect(chart.points).toEqual([
      { x: 8, y: 56 },
      { x: 120, y: 8 },
      { x: 232, y: 40 },
    ]);
    expect(buildWeekdayChart([])).toMatchObject({ linePoints: "", areaPath: "", points: [] });
  });
});

describe("recapState", () => {
  it("preserves server year order then requests all time once", () => {
    let state = beginYearsLoad(initialRecapState);
    expect(state.yearsLoaded).toBe(false);

    state = completeYears(state, { years: [2024, 2026, 2025] });
    expect(state.yearsLoaded).toBe(true);
    expect(state.years).toEqual([2024, 2026, 2025]);

    const [afterDefault, request] = takeDefaultRequest(state);
    expect(request).toBe(`/recap?${tz}`);
    expect(afterDefault.activeTab).toBe("all-time");
    expect(takeDefaultRequest(afterDefault)[1]).toBeNull();
  });

  it("selects custom for a complete query range", () => {
    const [state, request] = applyLocationQuery(initialRecapState, "?to=2026-07-19&from=2026-07-13");

    expect(state.hasSearchRange).toBe(true);
    expect(state.activeTab).toBe("custom");
    expect(state.customFrom).toBe("2026-07-13");
    expect(state.customTo).toBe("2026-07-19");
    expect(request).toBe(`/recap?from=2026-07-13&to=2026-07-19&${tz}`);
  });

  it("uses the first value for duplicate range keys", () => {
    const [state, request] = applyLocationQuery(
      initialRecapState,
      "?from=2026-07-13&from=2025-01-01&to=2026-07-19&to=2025-12-31",
    );

    expect(state.customFrom).toBe("2026-07-13");
    expect(state.customTo).toBe("2026-07-19");
    expect(request).toBe(`/recap?from=2026-07-13&to=2026-07-19&${tz}`);
  });

  it("ignores a one-sided query and re-arms the all-time request", () => {
    let state = completeYears(initialRecapState, { years: [2026] });
    [state] = takeDefaultRequest(state);

    const [next, request] = applyLocationQuery(state, "?from=2026-07-13");
    expect(request).toBeNull();
    expect(next.hasSearchRange).toBe(false);
    expect(next.activeTab).toBe("all-time");
    expect(next.customFrom).toBe("");
    expect(next.customTo).toBe("");
    expect(takeDefaultRequest(next)[1]).toBe(`/recap?${tz}`);
  });

  it("year tabs keep server order and load their range", () => {
    const state = completeYears(initialRecapState, { years: [2024, 2026, 2025] });

    expect(recapTabs(state).map((tab) => tab.key)).toEqual(["all-time", "year-2024", "year-2026", "year-2025", "custom"]);
    expect(recapTabs(state)[2].label).toBe("2026");

    const [next, request] = selectTab(state, "year-2026");
    expect(next.activeTab).toBe("year-2026");
    expect(request).toBe(`/recap?from=2026-01-01&to=2026-12-31&${tz}`);
    expect(selectTab(state, "all-time")[1]).toBe(`/recap?${tz}`);
  });

  it("custom range requires both bounds", () => {
    let [state, request] = selectTab(initialRecapState, "custom");
    expect(request).toBeNull();
    expect(state.activeTab).toBe("custom");

    state = setCustomFrom(state, "2026-07-13");
    expect(buildCustomNavigation(state)).toBeNull();
    expect(selectTab(state, "custom")[1]).toBeNull();

    state = setCustomTo(state, "2026-07-19");
    expect(selectTab(state, "custom")[1]).toBe(`/recap?from=2026-07-13&to=2026-07-19&${tz}`);
    expect(buildCustomNavigation(state)).toBe("/recap?from=2026-07-13&to=2026-07-19");
  });

  it("keeps old content while loading and clears it on failure", () => {
    const existing = recap(7);
    let state = completeLoad(initialRecapState, existing);
    state = beginLoad(state);
    expect(state.isLoading).toBe(true);
    expect(state.recap).toBe(existing);
    expect(state.error).toBe("");

    state = failLoad(state, "Failed to load recap.");
    expect(state.isLoading).toBe(false);
    expect(state.recap).toBeNull();
    expect(state.error).toBe("Failed to load recap.");

    const replacement = recap(9);
    state = completeLoad(beginLoad(state), replacement);
    expect(state.recap).toBe(replacement);
    expect(state.isLoading).toBe(false);
    expect(state.error).toBe("");
  });

  it("a years failure still resolves and allows the default load", () => {
    const state = failYears(initialRecapState, "Failed to load recap years.");
    expect(state.yearsLoaded).toBe(true);
    expect(state.years).toEqual([]);
    expect(state.error).toBe("Failed to load recap years.");
    expect(takeDefaultRequest(state)[1]).toBe(`/recap?${tz}`);
    expect(beginLoad(state).error).toBe("");
  });
});

describe("runInitialCustomFlow", () => {
  function setup(customCompletion?: Promise<void>) {
    const s = store();
    const requests: string[] = [];
    const years = deferred();
    const customRequest = s.step((current) => applyLocationQuery(current, "?from=2026-07-13&to=2026-07-19"))!;
    const flow = runInitialCustomFlow(
      customRequest,
      async () => {
        s.apply(beginYearsLoad);
        await years.promise;
        s.apply((current) => completeYears(current, { years: [2026] }));
      },
      () => s.step(takeDefaultRequest),
      (path) => {
        requests.push(path);
        return path === customRequest && customCompletion ? customCompletion : Promise.resolve();
      },
    );
    return { s, requests, years, customRequest, flow };
  }

  it("loads one fallback after years settle when the range was lost", async () => {
    const { s, requests, years, flow } = setup();

    expect(requests).toEqual([`/recap?from=2026-07-13&to=2026-07-19&${tz}`]);
    expect(s.step((current) => applyLocationQuery(current, "?from=2026-07-13"))).toBeNull();
    expect(s.step(takeDefaultRequest)).toBeNull();

    years.resolve();
    await flow;

    expect(requests).toEqual([`/recap?from=2026-07-13&to=2026-07-19&${tz}`, `/recap?${tz}`]);
    expect(s.step(takeDefaultRequest)).toBeNull();
  });

  it("starts the fallback before a stale custom load completes", async () => {
    const custom = deferred();
    const { s, requests, years, customRequest, flow } = setup(custom.promise);
    let done = false;
    void flow.then(() => {
      done = true;
    });

    expect(s.step((current) => applyLocationQuery(current, "?from=2026-07-13"))).toBeNull();
    years.resolve();
    await flush();

    expect(requests).toEqual([customRequest, `/recap?${tz}`]);
    expect(done).toBe(false);

    custom.resolve();
    await flow;
    expect(done).toBe(true);
  });

  it("does not double load when the custom range stays active", async () => {
    const { s, requests, years, flow } = setup();

    years.resolve();
    await flow;

    expect(requests).toEqual([`/recap?from=2026-07-13&to=2026-07-19&${tz}`]);
    expect(s.get().hasSearchRange).toBe(true);
    expect(s.step(takeDefaultRequest)).toBeNull();
  });
});
