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
  currentPage,
  emptySessionEditState,
  end,
  endDelete,
  failDelete,
  failEditSave,
  failListLoad,
  failStatsLoad,
  formatLocalDateTimeInput,
  getRowViews,
  isEditOpen,
  pageNumbers,
  sessionsDeleteRequest,
  setEditEndedAt,
  setEditStartedAt,
  setFrom,
  setOffset,
  setPlatform,
  setTitle,
  setTo,
  sortBy,
  start,
  togglePlatform,
  tryBeginStatsLoad,
  tryBuildEditSubmission,
  tryOpenEdit,
  tryParseLocal,
  groupRowViews,
  rangeLabel,
  selectCustomRange,
} from "./sessionsState";

const july13 = new Date("2026-07-13T00:00:00Z");

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
    mappedPlatform: null,
    mappedSource: null,
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
  it("opens a finished session with local date and time", () => {
    const localStart = new Date(2026, 7, 4, 18, 30, 15);
    const opened = tryOpenEdit(
      emptySessionEditState,
      session({ startedAt: localStart.toISOString(), endedAt: new Date(localStart.getTime() + 2 * 3_600_000).toISOString() }),
    );
    expect(opened).not.toBeNull();
    expect(opened!.startedAt).toBe("2026-08-04T18:30:15");
    expect(opened!.endedAt).toBe("2026-08-04T20:30:15");
    expect(isEditOpen(opened!)).toBe(true);
  });

  it("does not open an active session", () => {
    expect(tryOpenEdit(emptySessionEditState, session({ endedAt: null }))).toBeNull();
  });

  it("builds the owned session update request from valid local inputs", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = setEditStartedAt(state, "2026-08-04T18:30:15");
    state = setEditEndedAt(state, "2026-08-04T20:45:30");

    const { submission } = tryBuildEditSubmission(state);

    expect(submission).not.toBeNull();
    expect(submission!.path).toBe("/sessions/7");
    expect(submission!.method).toBe("PUT");
    expect(new Date(submission!.body.startedAt).getTime()).toBe(new Date(2026, 7, 4, 18, 30, 15).getTime());
    expect(new Date(submission!.body.endedAt).getTime()).toBe(new Date(2026, 7, 4, 20, 45, 30).getTime());
    expect(submission!.body.startedAt).toMatch(/^2026-08-04T18:30:15[+-]\d{2}:\d{2}$/);
  });

  it("rejects an end that is not later than the start", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = setEditStartedAt(state, "2026-08-04T20:45:30");
    state = setEditEndedAt(state, "2026-08-04T20:45:30");

    const result = tryBuildEditSubmission(state);

    expect(result.submission).toBeNull();
    expect(result.state.error).toBe("End time must be later than start time.");
  });

  it("rejects input that is not second-precision local date time", () => {
    let state = tryOpenEdit(emptySessionEditState, session())!;
    state = setEditStartedAt(state, "2026-08-04T18:30");
    expect(tryBuildEditSubmission(state).state.error).toBe("Enter a valid start and end date and time.");
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
    expect(state.endedAt).toBe("");
  });

  it("formats local date time inputs with zero padding", () => {
    expect(formatLocalDateTimeInput(new Date(2026, 0, 2, 3, 4, 5))).toBe("2026-01-02T03:04:05");
  });
});

describe("sessions state", () => {
  it("starts on the UTC Monday-Sunday week with exact URLs", () => {
    const state = createSessionsState(new Date("2026-07-13T23:30:00-07:00"));
    expect(state.preset).toBe("this-week");
    expect(state.from).toBe("2026-07-13");
    expect(state.to).toBe("2026-07-19");
    expect(Limit).toBe(10);
    expect(buildListRequest(state)).toBe("/sessions?limit=10&offset=0&from=2026-07-13&to=2026-07-19&sort=date&direction=desc");
    expect(buildStatsRequest(state)).toBe("/stats?from=2026-07-13&to=2026-07-19");
  });

  it("trims the debounced title, resets paging and only changes the list URL", () => {
    let state = setOffset(createSessionsState(july13), 30);
    state = setTitle(state, "  Hades II  ");
    state = applyDebouncedTitle(state, state.titleVersion)!;

    expect(state.offset).toBe(0);
    expect(state.debouncedTitle).toBe("Hades II");
    expect(buildListRequest(state)).toBe(
      "/sessions?limit=10&offset=0&title=Hades+II&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
    );
    expect(buildStatsRequest(state)).toBe("/stats?from=2026-07-13&to=2026-07-19");
  });

  it("form-encodes special characters in the title", () => {
    let state = setTitle(createSessionsState(july13), "A~B! (Test)*");
    state = applyDebouncedTitle(state, state.titleVersion)!;
    expect(buildListRequest(state)).toBe(
      "/sessions?limit=10&offset=0&title=A%7EB%21+%28Test%29*&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
    );
  });

  it("platform selection resets paging, filters both requests and toggles back to all", () => {
    let state = setPlatform(setOffset(createSessionsState(july13), 30), "ps5");
    expect(state.offset).toBe(0);
    expect(state.platform).toBe("ps5");
    expect(buildListRequest(state)).toBe(
      "/sessions?limit=10&offset=0&platform=ps5&from=2026-07-13&to=2026-07-19&sort=date&direction=desc",
    );
    expect(buildStatsRequest(state)).toBe("/stats?from=2026-07-13&to=2026-07-19&platform=ps5");

    state = togglePlatform(setOffset(state, 20), "ps5");
    expect(state.platform).toBe("");
    expect(state.offset).toBe(0);
    expect(buildListRequest(state)).not.toContain("platform");
    expect(buildStatsRequest(state)).not.toContain("platform");
  });

  it("rejects a stale debounce version so older work cannot win", () => {
    let state = setOffset(createSessionsState(july13), 20);
    state = setTitle(state, "older");
    const stale = state.titleVersion;
    state = setTitle(state, "newer");

    expect(applyDebouncedTitle(state, stale)).toBeNull();
    expect(state.offset).toBe(20);
    expect(state.debouncedTitle).toBe("");
    expect(applyDebouncedTitle(state, state.titleVersion)!.debouncedTitle).toBe("newer");
  });

  it("custom dates reset paging and stats require both bounds", () => {
    let state = setFrom(setOffset(createSessionsState(july13), 20), "");
    expect(state.offset).toBe(0);
    expect(state.preset).toBe("custom");
    expect(buildListRequest(state)).toBe("/sessions?limit=10&offset=0&to=2026-07-19&sort=date&direction=desc");
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
  ])("preset %s uses UTC calendar ranges and resets paging", (preset, from, to) => {
    const now = new Date("2026-07-13T23:30:00-07:00");
    const state = applyPreset(setOffset(createSessionsState(now), 20), preset, now);
    expect(state.preset).toBe(preset);
    expect(state.from).toBe(from);
    expect(state.to).toBe(to);
    expect(state.offset).toBe(0);
  });

  it("this-month handles February in a leap year", () => {
    const now = new Date("2028-02-10T12:00:00Z");
    const state = applyPreset(createSessionsState(now), "this-month", now);
    expect([state.from, state.to]).toEqual(["2028-02-01", "2028-02-29"]);
  });

  it("sort resets paging, toggles the active key and defaults a new key to descending", () => {
    let state = sortBy(setOffset(createSessionsState(july13), 20), "date");
    expect(state.sortKey).toBe("date");
    expect(state.sortDirection).toBe("asc");
    expect(state.offset).toBe(0);

    state = sortBy(setOffset(state, 10), "duration");
    expect(state.sortKey).toBe("duration");
    expect(state.sortDirection).toBe("desc");
    expect(state.offset).toBe(0);
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

  it("a completed list drives the showing text and four-page window", () => {
    const row = session({ startedAt: "1970-01-01T00:00:00Z", endedAt: "1970-01-01T00:00:00Z" });
    let state = beginListLoad(setOffset(createSessionsState(july13), 20));
    state = completeListLoad(state, { accountId: 1, sessions: [row], total: 47, limit: 10, offset: 20 });
    expect(state.isListLoading).toBe(false);
    expect(state.rows).toEqual([row]);
    expect(state.total).toBe(47);
    expect(start(state)).toBe(21);
    expect(end(state)).toBe(30);
    expect(currentPage(state)).toBe(3);
    expect(pageNumbers(state)).toEqual([2, 3, 4, 5]);
  });

  it("an empty list shows a single page and 0-0", () => {
    const state = createSessionsState(july13);
    expect(start(state)).toBe(0);
    expect(end(state)).toBe(0);
    expect(pageNumbers(state)).toEqual([1]);
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
    const now = new Date("2026-07-13T12:00:00Z");
    const row = session({
      startedAt: new Date(now.getTime() - 65 * 60_000).toISOString(),
      endedAt: now.toISOString(),
      platformKey: "manual",
      platformName: "Manual",
      capsuleUrl: "https://images.test/hades.jpg",
      ignored: true,
      mappedPlatform: "psn",
      mappedSource: "igdb",
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
    const now = new Date("2026-10-03T12:00:00Z");
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
    const state = selectCustomRange(createSessionsState(new Date("2026-10-03T12:00:00Z")));
    expect(state.preset).toBe("custom");
    expect([state.from, state.to]).toEqual(["2026-09-28", "2026-10-04"]);
  });
});
