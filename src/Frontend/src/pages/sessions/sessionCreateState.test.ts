import type { ManualMappingSearchResult } from "@/models/manual";
import {
  clearGame,
  completeSearch,
  formatMinutes,
  isLongDuration,
  failSave,
  openSessionCreate,
  parseInvariantDouble,
  selectGame,
  setDate,
  setHours,
  setMinutes,
  setMode,
  setPlatform,
  setQuery,
  submissionBlocker,
  tryBeginSave,
  tryBeginSearch,
  tryBuildSubmission,
  type SessionCreateMode,
} from "./sessionCreateState";
import { escapeDataString } from "./support";

const savedGame: ManualMappingSearchResult = {
  source: "local",
  gameId: "3H4h3",
  igdbId: null,
  title: "Hades II",
  platform: null,
  platformId: null,
  capsuleUrl: "https://images.test/hades.jpg",
};

const igdbGame: ManualMappingSearchResult = {
  source: "igdb",
  gameId: null,
  igdbId: "114283",
  title: "Hades II",
  platform: "PC",
  platformId: 6,
  capsuleUrl: "https://images.test/hades.jpg",
};

const open = (mode: SessionCreateMode) => openSessionCreate(mode, new Date(2026, 8, 8, 12, 0, 0));

describe("session create state", () => {
  it("defaults the date to the local calendar day", () => {
    expect(open("historical").date).toBe("2026-09-08");
    expect(open("live").hours).toBe("1");
    expect(open("live").minutes).toBe("0");
  });

  it("live saved game builds a start request with the selected platform", () => {
    const state = setPlatform(selectGame(open("live"), savedGame), "steam");
    const { submission } = tryBuildSubmission(state);
    expect(submission?.path).toBe("/manual/sessions/start");
    expect(submission?.method).toBe("POST");
    expect(submission?.kind).toBe("start");
    expect(submission?.body).toMatchObject({ gameId: "3H4h3", platformKey: "steam", igdbId: null });
  });

  it("live IGDB game carries identity metadata", () => {
    const state = setPlatform(selectGame(open("live"), igdbGame), "pc");
    const { submission } = tryBuildSubmission(state);
    expect(submission?.body).toEqual({
      title: "Hades II",
      gameId: null,
      platformKey: "pc",
      igdbId: "114283",
      capsuleUrl: "https://images.test/hades.jpg",
    });
  });

  it("historical entry builds date and whole-minute duration", () => {
    let state = setPlatform(selectGame(open("historical"), savedGame), "ps5");
    state = setMinutes(setHours(setDate(state, "2026-09-07"), "1"), "45");
    const { submission } = tryBuildSubmission(state);
    expect(submission?.path).toBe("/manual/entries");
    expect(submission?.body).toMatchObject({ date: "2026-09-07", durationMinutes: 105, gameId: "3H4h3", platformKey: "ps5" });
  });

  it("requires a game, platform and positive historical duration", () => {
    let state = open("live");
    let result = tryBuildSubmission(state);
    expect(result.submission).toBeNull();
    expect(result.state.error).toBe("Select a game.");

    state = selectGame(state, savedGame);
    result = tryBuildSubmission(state);
    expect(result.state.error).toBe("Select a platform.");

    state = setMinutes(setHours(setMode(setPlatform(state, "pc"), "historical"), "0"), "0");
    result = tryBuildSubmission(state);
    expect(result.state.error).toBe("Add how long you played.");

    result = tryBuildSubmission(setHours(state, "abc"));
    expect(result.state.error).toBe("Enter a valid duration.");
    result = tryBuildSubmission(setHours(state, "-1"));
    expect(result.state.error).toBe("Enter a valid duration.");
    result = tryBuildSubmission(setHours(setDate(state, ""), "1"));
    expect(result.state.error).toBe("Select a date.");
  });

  it("rejects minutes over 59 and hours over 99, and flags durations over 24 hours", () => {
    const state = setPlatform(selectGame(open("historical"), savedGame), "pc");
    expect(tryBuildSubmission(setMinutes(setHours(state, "1"), "60")).state.error).toBe("Minutes must be between 0 and 59.");
    expect(tryBuildSubmission(setMinutes(setHours(state, "100"), "0")).state.error).toBe("Hours must be 99 or less.");
    expect(tryBuildSubmission(setMinutes(setHours(state, "26"), "0")).submission?.body).toMatchObject({ durationMinutes: 1560 });
    expect(isLongDuration(1440)).toBe(false);
    expect(isLongDuration(1441)).toBe(true);
  });

  it("tracks whether the current query has been searched", () => {
    let state = setQuery(open("historical"), "Rez");
    expect(state.hasSearched).toBe(false);
    state = completeSearch(state, { query: "Rez", igdbAvailable: true, results: [] });
    expect(state.hasSearched).toBe(true);
    expect(setQuery(state, "Re").hasSearched).toBe(false);
  });

  it("fractional hours floor to whole minutes", () => {
    let state = setPlatform(selectGame(open("historical"), savedGame), "pc");
    state = setMinutes(setHours(state, "1.51"), "0");
    expect(tryBuildSubmission(state).submission?.body).toMatchObject({ durationMinutes: 90 });
  });

  it("search uses the manual catalog and changing the query clears the selection", () => {
    let state = setQuery(open("live"), "Hades & friends");
    const begin = tryBeginSearch(state);
    expect(begin.path).toBe("/manual/search?query=Hades%20%26%20friends");
    expect(begin.state.isSearching).toBe(true);

    state = completeSearch(begin.state, { query: "Hades & friends", igdbAvailable: true, results: [savedGame, igdbGame] });
    state = setQuery(selectGame(state, savedGame), "Celeste");
    expect(state.selectedGame).toBeNull();
    expect(state.results).toEqual([]);
  });

  it("an empty query asks for a title", () => {
    const begin = tryBeginSearch(setQuery(open("live"), "   "));
    expect(begin.path).toBeNull();
    expect(begin.state.searchError).toBe("Enter a game title.");
  });

  it("save cannot begin twice while the first request is pending", () => {
    const first = tryBeginSave(open("live"));
    expect(first).not.toBeNull();
    expect(tryBeginSave(first!)).toBeNull();
    expect(tryBeginSave(failSave(first!, "Failed."))).not.toBeNull();
  });
});

describe("save readiness", () => {
  it("reports the first missing field with the submission's own messages", () => {
    let state = openSessionCreate("historical", new Date(2026, 9, 4));
    expect(submissionBlocker(state)).toBe("Select a game.");
    state = selectGame(state, savedGame);
    expect(submissionBlocker(state)).toBe("Select a platform.");
    state = setPlatform(state, "pc");
    expect(submissionBlocker(state)).toBeNull();
    expect(submissionBlocker(setMinutes(setHours(state, "0"), "0"))).toBe("Add how long you played.");
    expect(submissionBlocker(setDate(state, ""))).toBe("Select a date.");
    expect(submissionBlocker(setMode(selectGame(openSessionCreate("historical", new Date()), savedGame), "live"))).toBe("Select a platform.");
  });

  it("clearing the game keeps the platform", () => {
    const state = clearGame(setPlatform(selectGame(openSessionCreate("historical", new Date()), savedGame), "pc"));
    expect(state.selectedGame).toBeNull();
    expect(state.platformKey).toBe("pc");
  });

  it("formats whole-minute durations", () => {
    expect(formatMinutes(15)).toBe("15m");
    expect(formatMinutes(60)).toBe("1h");
    expect(formatMinutes(150)).toBe("2h 30m");
  });
});

describe("helpers", () => {
  it("escapeDataString matches Uri.EscapeDataString", () => {
    expect(escapeDataString("A~B! (Test)*'")).toBe("A~B%21%20%28Test%29%2A%27");
  });

  it("parseInvariantDouble matches double.TryParse(Float, Invariant)", () => {
    expect(parseInvariantDouble(" 1.5 ")).toBe(1.5);
    expect(parseInvariantDouble("1e2")).toBe(100);
    expect(parseInvariantDouble(".5")).toBe(0.5);
    expect(parseInvariantDouble("")).toBeNull();
    expect(parseInvariantDouble("1,5")).toBeNull();
    expect(parseInvariantDouble("0x10")).toBeNull();
  });
});
