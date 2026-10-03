import type { ManualMappingSearchResult } from "@/models/manual";
import {
  completeSearch,
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
    expect(result.state.error).toBe("Duration must be at least 1 minute.");

    result = tryBuildSubmission(setHours(state, "abc"));
    expect(result.state.error).toBe("Enter a valid duration.");
    result = tryBuildSubmission(setHours(state, "-1"));
    expect(result.state.error).toBe("Enter a valid duration.");
    result = tryBuildSubmission(setHours(setDate(state, ""), "1"));
    expect(result.state.error).toBe("Select a date.");
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
