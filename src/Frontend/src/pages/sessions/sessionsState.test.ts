import { clientTimeZone } from "@/lib/dates";
import type { AccountSession } from "@/models/sessions";
import type { AccountStatsPeriod } from "@/models/stats";
import {
  ActiveManualSessionRequest,
  Limit,
  PlatformsRequest,
  SessionsRequestVersions,
  applyDebouncedTitle,
  applyPreset,
  beginDelete,
  beginEditSave,
  beginListLoad,
  beginStatsLoad,
  buildListRequest,
  buildStatsRequest,
  cancelEdit,
  completeActiveManualSession,
  completeDelete,
  completeEditSave,
  completeListLoad,
  completePlatforms,
  completeStatsLoad,
  createSessionsState,
  checkDuration,
  dailyBars,
  editDurationCheck,
  emptySessionEditState,
  endDelete,
  failDelete,
  failEditSave,
  failListLoad,
  failStatsLoad,
  formatLocalDateTimeInput,
  getRowViews,
  isEditOpen,
  buildDailyRequest,
  formatDurationInput,
  hasMore,
  listRequestPages,
  nextPageSize,
  platformFilterOptions,
  rangeDays,
  sessionsDeleteRequest,
  setEditDuration,
  setEditStartedAt,
  setFrom,
  setPlatform,
  setTitle,
  setTo,
  showingLabel,
  sortBy,
  togglePlatform,
  tryBeginStatsLoad,
  tryBuildEditSubmission,
  tryOpenEdit,
  tryParseLocal,
  groupRowViews,
  rangeLabel,
  selectCustomRange,
} from "./sessionsState";

// Local midnight, so the week and presets are the same in every time zone the tests run in.
const july13 = new Date(2026, 6, 13);
const tz = `tz=${encodeURIComponent(clientTimeZone())}`;

function session(overrides: Partial<AccountSession> = {}): AccountSession {
  const startedAt = new Date(Date.now() - 2 * 3_600_000);
  return {
    sessionId: 7,
    startedAt: startedAt.toISOString(),
    endedAt: new Date(startedAt.getTime() + 3_600_000).toISOString(),
    gameId: "42",
    platformKey: "steam",
    platformName: "Steam",
    game: "Hades II",
    capsuleUrl: null,
    ignored: false,
    ...overrides,
  };
}

const emptyStats = (overrides: Partial<AccountStatsPeriod> = {}): AccountStatsPeriod => ({
  totalSeconds: 3600,
  sessionCount: 1,
  topGameTitle: "Hades II",
  platforms: [],
  topGames: [],
  months: [],
  ...overrides,
});

describe("session edit state", () => {
  it("opens a finished session with local start and an h/m duration", () => {
    const localStart = new Date(2026, 7, 4, 18, 30, 15);
    const opened = tryOpenEdit(
      emptySessionEditState,
      session({ startedAt: localStart.toISOString(), endedAt: new Date(localStart.getTime() + 125 * 60_000 + 20_000).toISOString() }),
    );
    expect(opened).not.toBeNull();
    expect(opened!.startedAt).toBe("2026-08-04T18:30:15");
    expect(opened!.duration).toBe("2h 05m");
    expect(isEditOpen(opened!)).toBe(true);
  });

  it("does not open an active session", () => {
    expect(tryOpenEdit(emptySessionEditState, session({ endedAt: null }))).toBeNull();
  });

  it("an untouched duration keeps the exact original length, seconds included", () => {
    const localStart = new Date(2026, 7, 4, 18, 30, 15);
    let state = tryOpenEdit(
      emptySessionEditState,
      session({ startedAt: localStart.toISOString(), endedAt: new Date(localStart.getTime() + 65 * 60_000 + 30_000).toISOString() }),
    )!;
    state = setEditStartedAt(state, "2026-08-05T09:00:00");
    const { submission } = tryBuildEditSubmission(state);
    expect(submission!.path).toBe("/sessions/7");
    expect(submission!.method).toBe("PUT");
    expect(submission!.body.startedAt).toMatch(/^2026-08-05T09:00:00[+-]\d{2}:\d{2}$/);
    expect(new Date(submission!.body.endedAt).getTime()).toBe(new Date(2026, 7, 5, 10, 5, 30).getTime());
  });

  it("builds the end from the start plus a typed duration", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = setEditStartedAt(state, "2026-08-04T18:30:15");
    state = setEditDuration(state, " 2h 15m ");
    const { submission } = tryBuildEditSubmission(state);
    expect(new Date(submission!.body.startedAt).getTime()).toBe(new Date(2026, 7, 4, 18, 30, 15).getTime());
    expect(new Date(submission!.body.endedAt).getTime()).toBe(new Date(2026, 7, 4, 20, 45, 15).getTime());
  });

  it.each([
    ["", false, "Enter how long you played, e.g. 1h 30m."],
    ["0m", false, "A session needs at least 1 minute."],
    ["0h 0m", false, "A session needs at least 1 minute."],
    ["90", false, "Use hours and minutes, e.g. 1h 30m."],
    ["1.5h", false, "Use hours and minutes, e.g. 1h 30m."],
    ["75m", false, "Minutes go up to 59. Write 1h 15m instead."],
    ["1h 75m", false, "Minutes go up to 59. Write 2h 15m instead."],
    ["45m", true, ""],
    ["1H30M", true, ""],
    ["24h", true, ""],
    ["24h 1m", false, "A session can be at most 24 hours."],
    ["26h 05m", false, "A session can be at most 24 hours."],
  ])("duration %j: ok=%s", (value, ok, message) => {
    const check = checkDuration(value);
    expect(check.ok).toBe(ok);
    expect(check.message).toBe(message);
  });

  it("rejects a zero or malformed duration and never builds a request for it", () => {
    let state = setEditDuration(tryOpenEdit(emptySessionEditState, session())!, "0m");
    expect(editDurationCheck(state).ok).toBe(false);
    let result = tryBuildEditSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.error).toBe("A session needs at least 1 minute.");

    state = setEditDuration(state, "75m");
    result = tryBuildEditSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.error).toContain("Minutes go up to 59");
  });

  it("refuses a duration over 24h, typed or kept from an older session", () => {
    const state = setEditDuration(tryOpenEdit(emptySessionEditState, session())!, "26h 5m");
    expect(editDurationCheck(state)).toMatchObject({ ok: false, minutes: 1565, message: "A session can be at most 24 hours." });
    expect(tryBuildEditSubmission(state).submission).toBeNull();

    const startedAt = new Date(Date.now() - 30 * 3_600_000);
    const long = tryOpenEdit(emptySessionEditState, session({ startedAt: startedAt.toISOString(), endedAt: new Date().toISOString() }))!;
    expect(editDurationCheck(long).ok).toBe(false);
    expect(tryBuildEditSubmission(long).state.error).toBe("A session can be at most 24 hours.");
  });

  it("formats the duration field in hours and minutes", () => {
    expect(formatDurationInput(0)).toBe("0m");
    expect(formatDurationInput(45 * 60 + 59)).toBe("45m");
    expect(formatDurationInput(2 * 3600)).toBe("2h");
    expect(formatDurationInput(26 * 3600 + 5 * 60)).toBe("26h 05m");
  });

  it("rejects input that is not second-precision local date time", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = setEditStartedAt(state, "2026-08-04T18:30");
    expect(tryBuildEditSubmission(state).state.error).toBe("Enter a valid start date and time.");
    expect(tryParseLocal("2026-02-30T10:00:00")).toBeNull();
    expect(tryParseLocal("2026-02-28T24:00:00")).toBeNull();
  });

  it("keeps a failed edit open and closes it after a successful retry", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = beginEditSave(state);
    expect(state.isSaving).toBe(true);
    state = failEditSave(state, "Update failed.");
    expect(isEditOpen(state)).toBe(true);
    expect(state.isSaving).toBe(false);
    expect(state.error).toBe("Update failed.");

    state = completeEditSave();
    expect(isEditOpen(state)).toBe(false);
    expect(state.isSaving).toBe(false);
    expect(state.error).toBe("");
  });

  it("cancel discards the draft", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = setEditStartedAt(state, "2026-08-04T18:30:15");
    state = cancelEdit();
    expect(isEditOpen(state)).toBe(false);
    expect(state.startedAt).toBe("");
    expect(state.duration).toBe("");
  });

  it("formats local date time inputs with zero padding", () => {
    expect(formatLocalDateTimeInput(new Date(2026, 0, 2, 3, 4, 5))).toBe("2026-01-02T03:04:05");
  });
});

describe("sessions state", () => {
  it("starts on the viewer's Monday-Sunday week with exact URLs", () => {
    // Late Monday evening locally, already Tuesday in UTC for a viewer west of Greenwich.
    const state = createSessionsState(new Date(2026, 6, 13, 23, 30));
    expect(state.preset).toBe("this-week");
    expect(state.from).toBe("2026-07-13");
    expect(state.to).toBe("2026-07-19");
    expect(Limit).toBe(10);
    expect(buildListRequest(state)).toBe(`/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-19&sort=date&direction=desc&${tz}`);
    expect(buildStatsRequest(state)).toBe(`/stats?from=2026-07-13&to=2026-07-19&${tz}`);
  });

  it("trims the debounced title and filters the list and its stats alike", () => {
    let state = createSessionsState(july13);
    state = setTitle(state, "  Hades II  ");
    state = applyDebouncedTitle(state, state.titleVersion)!;
    expect(state.debouncedTitle).toBe("Hades II");
    expect(buildListRequest(state)).toBe(
      `/sessions?limit=10&offset=0&title=Hades+II&from=2026-07-13&to=2026-07-19&sort=date&direction=desc&${tz}`,
    );
    expect(buildStatsRequest(state)).toBe(`/stats?from=2026-07-13&to=2026-07-19&title=Hades+II&${tz}`);
    expect(buildDailyRequest(state)).toBe(`/stats/daily?from=2026-07-13&to=2026-07-19&title=Hades+II&${tz}`);
  });

  it("form-encodes special characters in the title", () => {
    let state = setTitle(createSessionsState(july13), "A~B! (Test)*");
    state = applyDebouncedTitle(state, state.titleVersion)!;
    expect(buildListRequest(state)).toBe(
      `/sessions?limit=10&offset=0&title=A%7EB%21+%28Test%29*&from=2026-07-13&to=2026-07-19&sort=date&direction=desc&${tz}`,
    );
  });

  it("platform selection filters both requests and toggles back to all", () => {
    let state = setPlatform(createSessionsState(july13), "ps5");
    expect(state.platform).toBe("ps5");
    expect(buildListRequest(state)).toBe(
      `/sessions?limit=10&offset=0&platform=ps5&from=2026-07-13&to=2026-07-19&sort=date&direction=desc&${tz}`,
    );
    expect(buildStatsRequest(state)).toBe(`/stats?from=2026-07-13&to=2026-07-19&platform=ps5&${tz}`);

    state = togglePlatform(state, "ps5");
    expect(state.platform).toBe("");
    expect(buildListRequest(state)).not.toContain("platform");
    expect(buildStatsRequest(state)).not.toContain("platform");
  });

  it("keeps every platform in the filter after one is picked", () => {
    let state = completePlatforms(createSessionsState(july13), {
      platforms: [
        { key: "pc", name: "PC" },
        { key: "ps5", name: "PlayStation 5" },
      ],
    });
    state = completeStatsLoad(state, {
      accountId: 1,
      stats: emptyStats({ platforms: [{ platformKey: "steam", platformName: "Steam", totalSeconds: 3600 }] }),
    });
    expect(platformFilterOptions(state)).toEqual([
      { key: "pc", label: "PC" },
      { key: "ps5", label: "PlayStation 5" },
      { key: "steam", label: "Steam" },
    ]);

    // Filtered stats only mention the selected platform, and here not even that one (no play in the range).
    state = completeStatsLoad(setPlatform(state, "switch"), { accountId: 1, stats: emptyStats({ platforms: [] }) });
    expect(platformFilterOptions(state).map((option) => option.key)).toEqual(["pc", "ps5", "switch"]);
  });

  it("rejects a stale debounce version so older work cannot win", () => {
    let state = createSessionsState(july13);
    state = setTitle(state, "older");
    const stale = state.titleVersion;
    state = setTitle(state, "newer");

    expect(applyDebouncedTitle(state, stale)).toBeNull();
    expect(state.debouncedTitle).toBe("");
    expect(applyDebouncedTitle(state, state.titleVersion)!.debouncedTitle).toBe("newer");
  });

  it("custom dates switch to custom and stats require both bounds", () => {
    let state = setFrom(createSessionsState(july13), "");
    expect(state.preset).toBe("custom");
    expect(buildListRequest(state)).toBe(`/sessions?limit=10&offset=0&to=2026-07-19&sort=date&direction=desc&${tz}`);
    expect(buildStatsRequest(state)).toBeNull();

    state = setTo(state, "");
    expect(buildListRequest(state)).toBe("/sessions?limit=10&offset=0&sort=date&direction=desc");
    expect(buildStatsRequest(state)).toBeNull();
  });

  it.each([
    ["today", "2026-07-14", "2026-07-14"],
    ["yesterday", "2026-07-13", "2026-07-13"],
    ["this-week", "2026-07-13", "2026-07-19"],
    ["last-week", "2026-07-06", "2026-07-12"],
    ["this-month", "2026-07-01", "2026-07-31"],
    ["this-year", "2026-01-01", "2026-12-31"],
    ["last-year", "2025-01-01", "2025-12-31"],
  ])("preset %s uses the viewer's calendar ranges", (preset, from, to) => {
    // 23:30 on Tuesday Jul 14 locally, whatever the date is in UTC.
    const now = new Date(2026, 6, 14, 23, 30);
    const state = applyPreset(createSessionsState(now), preset, now);
    expect(state.preset).toBe(preset);
    expect(state.from).toBe(from);
    expect(state.to).toBe(to);
  });

  it("this-month handles February in a leap year", () => {
    const now = new Date(2028, 1, 10, 12);
    const state = applyPreset(createSessionsState(now), "this-month", now);
    expect([state.from, state.to]).toEqual(["2028-02-01", "2028-02-29"]);
  });

  it("sort toggles the active key and defaults a new key to descending", () => {
    let state = sortBy(createSessionsState(july13), "date");
    expect(state.sortKey).toBe("date");
    expect(state.sortDirection).toBe("asc");

    state = sortBy(state, "duration");
    expect(state.sortKey).toBe("duration");
    expect(state.sortDirection).toBe("desc");
  });

  it("list and stats have independent loading, data and error lifecycles", () => {
    const stats = emptyStats();
    let state = beginStatsLoad(beginListLoad(createSessionsState(july13)));
    state = failListLoad(state, "List unavailable.");
    expect(state.isListLoading).toBe(false);
    expect(state.listError).toBe("List unavailable.");
    expect(state.rows).toEqual([]);
    expect(state.isStatsLoading).toBe(true);

    state = completeStatsLoad(state, { accountId: 1, stats });
    expect(state.isStatsLoading).toBe(false);
    expect(state.statsError).toBe("");
    expect(state.stats).toBe(stats);

    state = failStatsLoad(beginStatsLoad(state), "Stats unavailable.");
    expect(state.isStatsLoading).toBe(false);
    expect(state.statsError).toBe("Stats unavailable.");
    expect(state.stats).toBeNull();
    expect(state.listError).toBe("List unavailable.");
  });

  it("load more appends the next page without duplicates and drives the showing text", () => {
    const row = (sessionId: number) => session({ sessionId, startedAt: "1970-01-01T00:00:00Z", endedAt: "1970-01-01T00:00:00Z" });
    let state = beginListLoad(createSessionsState(july13));
    expect(state.isListLoading).toBe(true);
    state = completeListLoad(state, { sessions: Array.from({ length: 10 }, (_, index) => row(index + 1)), total: 23 });
    expect(showingLabel(state)).toBe("Showing 10 of 23 sessions");
    expect(hasMore(state)).toBe(true);
    expect(nextPageSize(state)).toBe(10);
    expect(listRequestPages(state, "more")).toEqual([
      `/sessions?limit=10&offset=10&from=2026-07-13&to=2026-07-19&sort=date&direction=desc&${tz}`,
    ]);

    state = beginListLoad(state, "more");
    expect(state.isLoadingMore).toBe(true);
    expect(state.isListLoading).toBe(false);
    expect(state.rows).toHaveLength(10);
    // Row 10 shifted onto the next page meanwhile; it must not render twice.
    state = completeListLoad(state, { sessions: [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20].map(row), total: 23 }, "more");
    expect(state.isLoadingMore).toBe(false);
    expect(state.rows.map((item) => item.sessionId)).toEqual(Array.from({ length: 20 }, (_, index) => index + 1));
    expect(nextPageSize(state)).toBe(3);

    state = failListLoad(beginListLoad(state, "more"), "Load failed.", "more");
    expect(state.rows).toHaveLength(20);
    expect(state.listError).toBe("Load failed.");

    state = completeListLoad(state, { sessions: [21, 22, 23].map(row), total: 23 }, "more");
    expect(showingLabel(state)).toBe("Showing 23 of 23 sessions");
    expect(hasMore(state)).toBe(false);
  });

  it("a refresh reloads every loaded row in pages of at most 100 and keeps rows visible", () => {
    const rows = Array.from({ length: 230 }, (_, index) => session({ sessionId: index + 1 }));
    const state = completeListLoad(createSessionsState(july13), { sessions: rows, total: 1284 });
    expect(showingLabel(state)).toBe("Showing 230 of 1,284 sessions");
    expect(listRequestPages(state, "refresh").map((path) => /limit=(\d+)&offset=(\d+)/.exec(path)!.slice(1).join("@"))).toEqual([
      "100@0",
      "100@100",
      "30@200",
    ]);
    expect(beginListLoad(state, "refresh").isListLoading).toBe(false);
    expect(listRequestPages(createSessionsState(july13), "refresh")).toHaveLength(1);
    expect(listRequestPages(state, "replace")).toEqual([
      `/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-19&sort=date&direction=desc&${tz}`,
    ]);
  });

  it("an empty list shows nothing more to load", () => {
    const state = createSessionsState(july13);
    expect(hasMore(state)).toBe(false);
    expect(showingLabel(completeListLoad(state, { sessions: [session()], total: 1 }))).toBe("Showing 1 of 1 session");
  });

  it("requests per-day stats only for week-sized ranges", () => {
    const now = new Date(2026, 9, 3, 12);
    const week = createSessionsState(now);
    expect(rangeDays(week)).toBe(7);
    expect(buildDailyRequest(week)).toBe(`/stats/daily?from=2026-09-28&to=2026-10-04&${tz}`);
    expect(buildDailyRequest(setPlatform(week, "ps5"))).toBe(`/stats/daily?from=2026-09-28&to=2026-10-04&platform=ps5&${tz}`);
    expect(buildDailyRequest(applyPreset(week, "today", now))).toBeNull();
    expect(buildDailyRequest(applyPreset(week, "this-month", now))).toBeNull();
    expect(buildDailyRequest(setFrom(week, ""))).toBeNull();
    expect(buildDailyRequest(setTo(week, "2026-09-01"))).toBeNull();
  });

  it("daily bars cover every day of the range and mark future days as upcoming", () => {
    const now = new Date(2026, 9, 3, 12);
    const state = completeStatsLoad(createSessionsState(now), { accountId: 1, stats: emptyStats() }, [
      { date: "2026-09-28", totalSeconds: 3600, sessionCount: 1, platforms: [] },
      { date: "2026-10-03", totalSeconds: 600, sessionCount: 1, platforms: [] },
    ]);
    const bars = dailyBars(state, now);
    expect(bars.map((bar) => bar.label)).toEqual(["Mon 28", "Tue 29", "Wed 30", "Thu 1", "Fri 2", "Sat 3", "Sun 4"]);
    expect(bars.map((bar) => bar.seconds)).toEqual([3600, 0, 0, 0, 0, 600, 0]);
    expect(bars.map((bar) => bar.upcoming)).toEqual([false, false, false, false, false, false, true]);
    expect(dailyBars(failStatsLoad(state, "x"), now)).toEqual([]);
  });

  it("stats load is skipped and cleared when either date bound is missing", () => {
    let state = completeStatsLoad(createSessionsState(july13), { accountId: 1, stats: emptyStats({ topGameTitle: null }) });
    state = setFrom(state, "");
    const skipped = tryBeginStatsLoad(state);
    expect(skipped.started).toBe(false);
    expect(skipped.state.isStatsLoading).toBe(false);
    expect(skipped.state.stats).toBeNull();

    const begun = tryBeginStatsLoad(setFrom(skipped.state, "2026-07-13"));
    expect(begun.started).toBe(true);
    expect(begun.state.isStatsLoading).toBe(true);
  });

  it("request versions reject delayed stats and active-session responses", () => {
    const versions = new SessionsRequestVersions();
    const stats = versions.beginStatsRequest();
    versions.beginStatsRequest();
    expect(versions.isCurrentStatsRequest(stats)).toBe(false);

    const stale = versions.beginActiveManualSessionRequest();
    const current = versions.beginActiveManualSessionRequest();
    expect(versions.isCurrentActiveManualSessionRequest(stale)).toBe(false);
    expect(versions.isCurrentActiveManualSessionRequest(current)).toBe(true);
  });

  it("delete is confirm gated, uses the exact endpoint and refreshes only on success", () => {
    const request = sessionsDeleteRequest(7);
    expect(request).toEqual({ confirmation: "Remove this session entry?", path: "/sessions/7", method: "DELETE" });

    let state = beginDelete(createSessionsState(july13), 7);
    expect(state.deletingSessionIds.has(7)).toBe(true);
    expect(state.listError).toBe("");

    state = endDelete(completeDelete(state), 7);
    expect(state.refreshVersion).toBe(1);
    expect(state.deletingSessionIds.has(7)).toBe(false);

    state = endDelete(failDelete(beginDelete(state, 8), "Failed to remove session."), 8);
    expect(state.listError).toBe("Failed to remove session.");
    expect(state.refreshVersion).toBe(1);
  });

  it("row views use platform name, capsule, ignored flag, hash and short duration", () => {
    const now = new Date(2026, 6, 13, 12);
    const row = session({
      startedAt: new Date(now.getTime() - 65 * 60_000).toISOString(),
      endedAt: now.toISOString(),
      platformKey: "manual",
      platformName: "Manual",
      capsuleUrl: "https://images.test/hades.jpg",
      ignored: true,
    });
    const state = completeListLoad(createSessionsState(now), { accountId: 1, sessions: [row], total: 1, limit: 10, offset: 0 });

    const [view] = getRowViews(state, now);
    expect(view.sessionId).toBe(7);
    expect(view.gamePath).toBe("/games/3H4h3");
    expect(view.title).toBe("Hades II");
    expect(view.platform).toBe("Manual");
    expect(view.duration).toBe("1h 05m");
    expect(view.imageUrl).toBe("https://images.test/hades.jpg");
    expect(view.ignored).toBe(true);
  });

  it("only finished sessions are editable", () => {
    const finished = session();
    const active = { ...finished, sessionId: 8, endedAt: null };
    const state = completeListLoad(createSessionsState(new Date()), {
      accountId: null,
      sessions: [finished, active],
      total: 2,
      limit: 10,
      offset: 0,
    });
    const views = getRowViews(state, new Date());
    expect(views[0].canEdit).toBe(true);
    expect(views[1].canEdit).toBe(false);
  });

  it("platform catalog and active manual session have dedicated requests", () => {
    let state = completePlatforms(createSessionsState(new Date()), {
      platforms: [
        { key: "pc", name: "PC" },
        { key: "ps5", name: "PS5" },
      ],
    });
    const active = {
      sessionId: 9,
      gameId: "3H4h3",
      title: "Hades II",
      platformKey: "pc",
      platformName: "PC",
      startedAt: new Date().toISOString(),
      capsuleUrl: null,
    };
    state = completeActiveManualSession(state, { session: active });
    expect(PlatformsRequest).toBe("/manual/platforms");
    expect(ActiveManualSessionRequest).toBe("/manual/sessions/current");
    expect(state.platforms.map((item) => item.key)).toEqual(["pc", "ps5"]);
    expect(state.activeManualSession).toBe(active);
  });
});

describe("sessions list presentation", () => {
  const at = (year: number, month: number, day: number, hour: number) => new Date(year, month - 1, day, hour, 0, 0);
  const played = (sessionId: number, start: Date, minutes: number) =>
    session({ sessionId, startedAt: start.toISOString(), endedAt: new Date(start.getTime() + minutes * 60_000).toISOString() });

  it("groups date-sorted rows by local start day with counts and totals", () => {
    const now = at(2026, 10, 3, 20);
    const rows = [
      played(1, at(2026, 10, 3, 10), 10),
      played(2, at(2026, 9, 30, 10), 60),
      played(3, at(2026, 9, 30, 8), 25),
    ];
    const state = completeListLoad(createSessionsState(now), { accountId: 1, sessions: rows, total: 3, limit: 10, offset: 0 });
    const groups = groupRowViews(state, getRowViews(state, now), now);
    expect(groups.map((group) => group.label)).toEqual(["TODAY", "WEDNESDAY, SEP 30"]);
    expect(groups[0].meta).toBe("1 session · 10m");
    expect(groups[1].meta).toBe("2 sessions · 1h 25m");
    expect(groups[1].rows.map((row) => row.sessionId)).toEqual([2, 3]);
  });

  it("keeps a single unlabeled group when sorted by duration", () => {
    const now = new Date();
    let state = completeListLoad(createSessionsState(now), { accountId: 1, sessions: [session({ sessionId: 1 }), session({ sessionId: 2 })], total: 2, limit: 10, offset: 0 });
    state = sortBy(state, "duration");
    const groups = groupRowViews(state, getRowViews(state, now), now);
    expect(groups).toHaveLength(1);
    expect(groups[0].label).toBe("");
    expect(groups[0].meta).toBe("2 sessions · 2h 00m");
    expect(groupRowViews(state, [], now)).toEqual([]);
  });

  it("labels the selected range like the design", () => {
    const now = new Date(2026, 9, 3, 12);
    const base = createSessionsState(now);
    expect(rangeLabel(base)).toBe("Sep 28 – Oct 4, 2026");
    expect(rangeLabel(applyPreset(base, "today", now))).toBe("Today · Oct 3, 2026");
    expect(rangeLabel(applyPreset(base, "yesterday", now))).toBe("Yesterday · Oct 2, 2026");
    expect(rangeLabel(applyPreset(base, "last-week", now))).toBe("Sep 21 – 27, 2026");
    expect(rangeLabel(applyPreset(base, "this-month", now))).toBe("October 2026");
    expect(rangeLabel(applyPreset(base, "last-year", now))).toBe("2025");
    expect(rangeLabel(setFrom(setTo(base, "2027-01-04"), "2026-12-29"))).toBe("Dec 29, 2026 – Jan 4, 2027");
    expect(rangeLabel(setTo(setFrom(base, ""), ""))).toBe("All time");
    expect(rangeLabel(setTo(base, ""))).toBe("From Sep 28, 2026");
  });

  it("switching to a custom range keeps the bounds", () => {
    const state = selectCustomRange(createSessionsState(new Date(2026, 9, 3, 12)));
    expect(state.preset).toBe("custom");
    expect([state.from, state.to]).toEqual(["2026-09-28", "2026-10-04"]);
  });
});
