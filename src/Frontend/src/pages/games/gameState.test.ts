import { clientTimeZone } from "@/lib/dates";
import type { AccountSession, AccountSessionsResponse } from "@/models/sessions";
import {
  activeYear,
  beginGame,
  buildIgnoreSubmission,
  buildRatingSubmission,
  completeFirstSessions,
  completeSessionsPage,
  completeStats,
  displayTitle,
  failDetails,
  gameImage,
  gameRequestsFor,
  lastPlayedLabel,
  monthlyStats,
  recentTotalPages,
  sessionsPagePath,
  totalSeconds,
  yearlyStats,
} from "./gameState";

function session(id: number, startedAt: string, endedAt: string, capsuleUrl: string | null = null): AccountSession {
  return {
    sessionId: id,
    startedAt,
    endedAt,
    gameId: "42",
    platformKey: "steam",
    platformName: "Steam",
    game: "Game",
    capsuleUrl,
    ignored: false,
  };
}

function page(sessions: AccountSession[], total: number, offset: number): AccountSessionsResponse {
  return { accountId: null, sessions, total, limit: 5, offset };
}

describe("gameState", () => {
  it("builds the detail, stats and paged session requests", () => {
    const requests = gameRequestsFor("3H4h3");

    expect(requests.gameId).toBe("42");
    expect(requests.details).toBe("/games/3H4h3");
    expect(requests.stats).toBe(`/games/3H4h3/stats?tz=${encodeURIComponent(clientTimeZone())}`);
    // Every session of the game, with no fixed start date.
    expect(sessionsPagePath(requests, 1)).toBe("/sessions?gameId=3H4h3&sort=date&direction=desc&limit=5&offset=0");
    expect(sessionsPagePath(requests, 2)).toBe("/sessions?gameId=3H4h3&sort=date&direction=desc&limit=5&offset=5");
    expect(requests.ignoreStatus).toBe("/games/ignored?gameId=3H4h3");
    expect(requests.rating).toBe("/games/rating?gameId=3H4h3");
  });

  it.each([null, "", "not-a-hash"])("reports an invalid link for %j", (hash) => {
    const state = beginGame(hash, new Date());

    expect(state.requests).toBeNull();
    expect(state.gameDetailsError).toBe("Invalid game link.");
    expect(state.gameError).toBe("Invalid game link.");
    expect(state.ignoreError).toBe("Invalid game link.");
    expect(state.ratingError).toBe("Invalid game link.");
    expect(state.sessionsLoaded).toBe(true);
    expect(state.isLoading).toBe(false);
    expect(displayTitle(state)).toBe("Unknown game");
  });

  it("starts loading for a valid hash", () => {
    const state = beginGame("3H4h3", new Date(2026, 6, 18));

    expect(state.requests?.gameHash).toBe("3H4h3");
    expect(state.isLoading).toBe(true);
    expect(state.isIgnoreChecking).toBe(true);
    expect(state.isRatingLoading).toBe(true);
    expect(state.sessionsLoaded).toBe(false);
    expect(displayTitle(state)).toBe("Loading game...");
    expect(displayTitle(failDetails(state, "boom"))).toBe("Unknown game");
  });

  it("keeps the recent page total and page independent of charts", () => {
    let state = beginGame("3H4h3", new Date(2026, 6, 18));
    state = completeFirstSessions(state, page([session(1, "2026-07-18T08:00:00Z", "2026-07-18T10:00:00Z", "recent.jpg")], 11, 0));

    expect(recentTotalPages(state)).toBe(3);
    expect(state.recentSessions).toHaveLength(1);
    expect(gameImage(state)).toBe("recent.jpg");
    expect(state.isLoading).toBe(false);
    expect(state.sessionsLoaded).toBe(true);

    state = completeSessionsPage(state, 3, page([session(11, "2026-07-08T08:00:00Z", "2026-07-08T09:00:00Z")], 11, 10));

    expect(state.recentPage).toBe(3);
    expect(state.recentSessions).toHaveLength(1);
    expect(recentTotalPages(state)).toBe(3);
    expect(state.isRecentPageLoading).toBe(false);
    expect(gameImage(state)).toBeNull();
  });

  it("builds chart buckets from server stats", () => {
    let state = beginGame("3H4h3", new Date(2026, 6, 18));
    expect(yearlyStats(state)).toEqual([]);
    expect(activeYear(state)).toBe(2026);

    state = completeStats(state, {
      totalSeconds: 7200,
      sessionCount: 3,
      lastPlayedAt: new Date(2026, 6, 18, 8).toISOString(),
      years: [
        { year: 2025, seconds: 1800 },
        { year: 2026, seconds: 5400 },
      ],
      months: [
        { year: 2026, month: 6, seconds: 5400 },
        { year: 2025, month: 0, seconds: 1800 },
      ],
    });

    expect(totalSeconds(state)).toBe(7200);
    expect(lastPlayedLabel(state)).toBe("Jul 18, 2026");
    const years = yearlyStats(state);
    expect(years.map((item) => item.key)).toEqual(["2025", "2026"]);
    expect(years.find((item) => item.year === 2025)?.hours).toBe(0.5);
    expect(years.find((item) => item.year === 2026)?.hours).toBe(1.5);
    const months = monthlyStats(state);
    expect(months).toHaveLength(12);
    expect(months[5]).toMatchObject({ key: "2026-5", label: "Jun", hours: 1.5 });
    expect(activeYear(state)).toBe(2026);
    expect(activeYear({ ...state, selectedYear: 1999 })).toBe(2026);
    expect(activeYear({ ...state, selectedYear: 2025 })).toBe(2025);
  });

  it("starts the year chart at the first year played, before 2025 too", () => {
    const state = completeStats(beginGame("3H4h3", new Date(2026, 6, 18)), {
      totalSeconds: 3600,
      sessionCount: 1,
      lastPlayedAt: "2022-03-01T08:00:00Z",
      years: [
        { year: 2022, seconds: 3600 },
        { year: 2023, seconds: 0 },
        { year: 2024, seconds: 0 },
        { year: 2025, seconds: 0 },
        { year: 2026, seconds: 0 },
      ],
      months: [],
    });

    expect(yearlyStats(state).map((item) => item.year)).toEqual([2022, 2023, 2024, 2025, 2026]);
    expect(yearlyStats(state)[0].hours).toBe(1);
    expect(activeYear({ ...state, selectedYear: 2022 })).toBe(2022);
  });

  it("builds tracking and rating submissions", () => {
    const state = beginGame("3H4h3", new Date());

    expect(buildIgnoreSubmission(state, false)).toEqual({
      path: "/games/ignored",
      method: "POST",
      body: { gameId: "3H4h3" },
    });
    expect(buildIgnoreSubmission(state, true).method).toBe("DELETE");
    expect(buildRatingSubmission(state, 5)).toEqual({
      path: "/games/rating",
      method: "POST",
      body: { gameId: "3H4h3", rating: 5 },
    });
    expect(buildRatingSubmission(state, null)).toEqual({
      path: "/games/rating",
      method: "DELETE",
      body: { gameId: "3H4h3" },
    });
  });
});
