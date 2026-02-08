import { afterAll, beforeEach, expect, mock, test } from "bun:test";

import { recapRepository } from "../src/modules/recap/recap.repository";
import { gamesService } from "../src/modules/games";
import { recapService } from "../src/modules/recap/recap.service";

type SessionRow = {
  id: number;
  started_at: Date;
  ended_at: Date | null;
  game_id: number;
  title: string;
  capsule_url: string | null;
  platform: string;
  game_platform: string;
  game_canonical_key: string;
};

type FirstSessionRow = {
  game_id: number;
  first_started_at: Date;
  title: string;
  capsule_url: string | null;
  platform: string;
  game_platform: string;
  game_canonical_key: string;
};

type Edge = { game_id: number; connected_game_id: number };

type Mapping = {
  canonical_title: string;
  mapped_game_id: number | null;
  mapped_title: string | null;
  mapped_platform: string | null;
  mapped_capsule_url: string | null;
  mapped_canonical_key: string | null;
};

const origRepo = { ...recapRepository };
const origGames = { ...gamesService };

const repoMock = {
  listRecapSessions: mock(async (): Promise<SessionRow[]> => []),
  listRecapYears: mock(async (): Promise<{ year: number }[]> => []),
  listFirstSessionsByGame: mock(async (): Promise<FirstSessionRow[]> => []),
  loadManualMappings: mock(async (): Promise<Mapping[]> => []),
};

const gamesMock = {
  listConnectionEdges: mock(async (): Promise<Edge[]> => []),
};

let nextId = 0;

const s = (
  gameId: number,
  title: string,
  start: string,
  end: string | null,
  platform = "steam"
): SessionRow => ({
  id: ++nextId,
  started_at: new Date(start),
  ended_at: end ? new Date(end) : null,
  game_id: gameId,
  title,
  capsule_url: null,
  platform,
  game_platform: platform,
  game_canonical_key: `${platform}:${gameId}`,
});

const fs = (
  gameId: number,
  title: string,
  startedAt: string,
  platform = "steam"
): FirstSessionRow => ({
  game_id: gameId,
  first_started_at: new Date(startedAt),
  title,
  capsule_url: null,
  platform,
  game_platform: platform,
  game_canonical_key: `${platform}:${gameId}`,
});

const JAN_2024 = {
  from: "2024-01-01",
  to: "2024-01-31",
  startedAt: new Date("2024-01-01T00:00:00Z"),
  endedAt: new Date("2024-02-01T00:00:00Z"),
};

const recap = (
  sessions: SessionRow[],
  opts: {
    range?: typeof JAN_2024;
    firstSessions?: FirstSessionRow[];
    edges?: Edge[];
    mappings?: Mapping[];
  } = {}
) => {
  const range = opts.range ?? JAN_2024;
  repoMock.listRecapSessions.mockResolvedValueOnce(sessions);
  repoMock.listFirstSessionsByGame.mockResolvedValueOnce(
    opts.firstSessions ?? []
  );
  gamesMock.listConnectionEdges.mockResolvedValueOnce(opts.edges ?? []);
  if (opts.mappings) {
    repoMock.loadManualMappings.mockResolvedValue(opts.mappings);
  }
  return recapService.fetchRecap({
    userId: 1,
    from: range.from,
    to: range.to,
    startedAt: range.startedAt,
    endedAt: range.endedAt,
  });
};

beforeEach(() => {
  nextId = 0;
  repoMock.listRecapSessions.mockReset();
  repoMock.listRecapYears.mockReset();
  repoMock.listFirstSessionsByGame.mockReset();
  repoMock.loadManualMappings.mockReset();
  gamesMock.listConnectionEdges.mockReset();

  repoMock.listRecapYears.mockResolvedValue([]);
  repoMock.listFirstSessionsByGame.mockResolvedValue([]);
  repoMock.loadManualMappings.mockResolvedValue([]);
  gamesMock.listConnectionEdges.mockResolvedValue([]);

  Object.assign(recapRepository, repoMock);
  Object.assign(gamesService, { listConnectionEdges: gamesMock.listConnectionEdges });
});

afterAll(() => {
  Object.assign(recapRepository, origRepo);
  Object.assign(gamesService, origGames);
});

test("empty recap returns zeroed stats", async () => {
  const result = await recap([]);

  expect(result.range).toEqual({ from: "2024-01-01", to: "2024-01-31" });
  expect(result.totalSessions).toBe(0);
  expect(result.totalSeconds).toBe(0);
  expect(result.averageSessionSeconds).toBe(0);
  expect(result.medianSessionSeconds).toBe(0);
  expect(result.longestSessionSeconds).toBe(0);
  expect(result.shortestSessionSeconds).toBe(0);
  expect(result.averageSessionsPerDay).toBe(0);
  expect(result.averageSessionsPerWeek).toBe(0);
  expect(result.averageSessionsPerMonth).toBe(0);
  expect(result.longestStreakDays).toBe(0);
  expect(result.daysPlayed).toBe(0);
  expect(result.daysNotPlayed).toBe(31);
  expect(result.uniqueGames).toBe(0);
  expect(result.topGameByTime).toBeNull();
  expect(result.topGameBySessions).toBeNull();
  expect(result.topGameByAverageSession).toBeNull();
  expect(result.gameWithLongestSession).toBeNull();
  expect(result.mostBingedGame).toBeNull();
  expect(result.mostUsedPlatform).toBeNull();
  expect(result.mostActiveDay).toBeNull();
  expect(result.mostActiveMonth).toBeNull();
  expect(result.peakHour).toBeNull();
  expect(result.newGamesTried).toBe(0);
  expect(result.abandonedGames).toBe(0);
  expect(result.returnRateGames).toBe(0);
  expect(result.timeOnTop3Percent).toBe(0);
  expect(result.weekendSeconds).toBe(0);
  expect(result.weekdaySeconds).toBe(0);
  expect(result.topGamesByTime).toEqual([]);
  expect(result.gamesTable).toEqual([]);
  expect(result.platformTotals).toEqual([]);
});

test("single session produces correct stats", async () => {
  // Jan 15 2024 is a Monday
  const sessions = [
    s(1, "Solo", "2024-01-15T10:00:00Z", "2024-01-15T11:00:00Z"),
  ];
  const result = await recap(sessions, {
    firstSessions: [fs(1, "Solo", "2024-01-15T10:00:00Z")],
  });

  expect(result.totalSessions).toBe(1);
  expect(result.totalSeconds).toBe(3600);
  expect(result.averageSessionSeconds).toBe(3600);
  expect(result.medianSessionSeconds).toBe(3600);
  expect(result.longestSessionSeconds).toBe(3600);
  expect(result.shortestSessionSeconds).toBe(3600);
  expect(result.longestStreakDays).toBe(1);
  expect(result.daysPlayed).toBe(1);
  expect(result.daysNotPlayed).toBe(30);
  expect(result.uniqueGames).toBe(1);
  expect(result.abandonedGames).toBe(1);
  expect(result.newGamesTried).toBe(1);
  expect(result.topGameByTime?.title).toBe("Solo");
  expect(result.topGameBySessions?.title).toBe("Solo");
  expect(result.gameWithLongestSession?.title).toBe("Solo");
  expect(result.weekdaySeconds).toBe(3600);
  expect(result.weekendSeconds).toBe(0);
});

test("computes total, average, longest, and shortest correctly", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"), // 3600s
    s(2, "B", "2024-01-02T14:00:00Z", "2024-01-02T14:30:00Z"), // 1800s
    s(1, "A", "2024-01-03T20:00:00Z", "2024-01-03T22:00:00Z"), // 7200s
  ];

  const result = await recap(sessions);

  expect(result.totalSessions).toBe(3);
  expect(result.totalSeconds).toBe(12600);
  expect(result.averageSessionSeconds).toBe(4200);
  expect(result.longestSessionSeconds).toBe(7200);
  expect(result.shortestSessionSeconds).toBe(1800);
});

test("median with odd number of sessions", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T00:00:00Z", "2024-01-01T00:01:40Z"), // 100s
    s(2, "B", "2024-01-02T00:00:00Z", "2024-01-02T00:05:00Z"), // 300s
    s(3, "C", "2024-01-03T00:00:00Z", "2024-01-03T00:03:20Z"), // 200s
  ];

  const result = await recap(sessions);
  expect(result.medianSessionSeconds).toBe(200);
});

test("median with even number of sessions", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T00:00:00Z", "2024-01-01T00:01:40Z"), // 100s
    s(2, "B", "2024-01-02T00:00:00Z", "2024-01-02T00:03:20Z"), // 200s
    s(3, "C", "2024-01-03T00:00:00Z", "2024-01-03T00:05:00Z"), // 300s
    s(4, "D", "2024-01-04T00:00:00Z", "2024-01-04T00:06:40Z"), // 400s
  ];

  const result = await recap(sessions);
  expect(result.medianSessionSeconds).toBe(250);
});

test("longest streak counts consecutive UTC days", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"),
    s(1, "A", "2024-01-02T10:00:00Z", "2024-01-02T11:00:00Z"),
    s(1, "A", "2024-01-03T10:00:00Z", "2024-01-03T11:00:00Z"),
    // gap on Jan 4
    s(1, "A", "2024-01-05T10:00:00Z", "2024-01-05T11:00:00Z"),
    s(1, "A", "2024-01-06T10:00:00Z", "2024-01-06T11:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.longestStreakDays).toBe(3);
  expect(result.daysPlayed).toBe(5);
});

test("days played and not played within range", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"),
    s(1, "A", "2024-01-15T10:00:00Z", "2024-01-15T11:00:00Z"),
    s(1, "A", "2024-01-31T10:00:00Z", "2024-01-31T11:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.daysPlayed).toBe(3);
  expect(result.daysNotPlayed).toBe(28);
});

test("weekend and weekday seconds are split correctly", async () => {
  // Jan 6 2024 = Saturday, Jan 8 2024 = Monday
  const sessions = [
    s(1, "A", "2024-01-06T10:00:00Z", "2024-01-06T11:00:00Z"), // Sat 1h
    s(1, "A", "2024-01-08T10:00:00Z", "2024-01-08T10:30:00Z"), // Mon 30m
  ];

  const result = await recap(sessions);
  expect(result.weekendSeconds).toBe(3600);
  expect(result.weekdaySeconds).toBe(1800);
});

test("sessions by weekday uses Monday=0 indexing", async () => {
  // Jan 1 2024 = Monday, Jan 7 2024 = Sunday
  const sessions = [
    s(1, "A", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"),
    s(1, "A", "2024-01-01T14:00:00Z", "2024-01-01T15:00:00Z"),
    s(1, "A", "2024-01-07T10:00:00Z", "2024-01-07T11:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.sessionsByWeekday[0].sessionCount).toBe(2); // Monday
  expect(result.sessionsByWeekday[6].sessionCount).toBe(1); // Sunday
});

test("peak hour identifies the most common playing hour", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T14:00:00Z", "2024-01-01T15:00:00Z"),
    s(1, "A", "2024-01-02T14:30:00Z", "2024-01-02T15:30:00Z"),
    s(1, "A", "2024-01-03T20:00:00Z", "2024-01-03T21:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.peakHour?.hour).toBe(14);
  expect(result.peakHour?.sessionCount).toBe(2);
});

test("most active day and month are correctly identified", async () => {
  const sessions = [
    s(1, "A", "2024-01-15T10:00:00Z", "2024-01-15T11:00:00Z"),
    s(2, "B", "2024-01-15T14:00:00Z", "2024-01-15T15:00:00Z"),
    s(1, "A", "2024-01-15T20:00:00Z", "2024-01-15T21:00:00Z"),
    s(1, "A", "2024-01-20T10:00:00Z", "2024-01-20T11:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.mostActiveDay?.date).toBe("2024-01-15");
  expect(result.mostActiveDay?.sessionCount).toBe(3);
  expect(result.mostActiveMonth?.month).toBe("2024-01");
  expect(result.mostActiveMonth?.sessionCount).toBe(4);
});

test("top game by time and by sessions can differ", async () => {
  const sessions = [
    s(1, "Long Game", "2024-01-01T10:00:00Z", "2024-01-01T13:00:00Z"),
    s(1, "Long Game", "2024-01-02T10:00:00Z", "2024-01-02T12:00:00Z"),
    s(2, "Frequent", "2024-01-03T10:00:00Z", "2024-01-03T10:30:00Z"),
    s(2, "Frequent", "2024-01-04T10:00:00Z", "2024-01-04T10:30:00Z"),
    s(2, "Frequent", "2024-01-05T10:00:00Z", "2024-01-05T10:30:00Z"),
    s(2, "Frequent", "2024-01-06T10:00:00Z", "2024-01-06T10:30:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.topGameByTime?.title).toBe("Long Game");
  expect(result.topGameByTime?.totalSeconds).toBe(18000);
  expect(result.topGameBySessions?.title).toBe("Frequent");
  expect(result.topGameBySessions?.sessionCount).toBe(4);
});

test("topGameByAverageSession picks highest average", async () => {
  const sessions = [
    s(1, "Deep Diver", "2024-01-01T10:00:00Z", "2024-01-01T13:00:00Z"),
    s(1, "Deep Diver", "2024-01-02T10:00:00Z", "2024-01-02T13:00:00Z"),
    s(2, "Quick Plays", "2024-01-03T10:00:00Z", "2024-01-03T10:30:00Z"),
    s(2, "Quick Plays", "2024-01-04T10:00:00Z", "2024-01-04T10:30:00Z"),
    s(2, "Quick Plays", "2024-01-05T10:00:00Z", "2024-01-05T10:30:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.topGameByAverageSession?.title).toBe("Deep Diver");
  expect(result.topGameByAverageSession?.averageSessionSeconds).toBe(10800);
});

test("gameWithLongestSession identifies the right game", async () => {
  const sessions = [
    s(1, "Short Plays", "2024-01-01T10:00:00Z", "2024-01-01T10:30:00Z"),
    s(1, "Short Plays", "2024-01-02T10:00:00Z", "2024-01-02T10:30:00Z"),
    s(2, "Marathon", "2024-01-03T10:00:00Z", "2024-01-03T15:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.gameWithLongestSession?.title).toBe("Marathon");
  expect(result.gameWithLongestSession?.sessionSeconds).toBe(18000);
});

test("game connections merge games into groups", async () => {
  const sessions = [
    s(1, "Hades PC", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"),
    s(1, "Hades PC", "2024-01-02T10:00:00Z", "2024-01-02T11:00:00Z"),
    s(2, "Hades PS5", "2024-01-03T10:00:00Z", "2024-01-03T13:00:00Z"),
  ];
  const edges: Edge[] = [{ game_id: 1, connected_game_id: 2 }];

  const result = await recap(sessions, { edges });

  expect(result.uniqueGames).toBe(1);
  expect(result.topGameByTime?.totalSeconds).toBe(18000);
  expect(result.topGameByTime?.sessionCount).toBe(3);
  // Representative is game with highest individual total (Hades PS5: 10800s > 7200s)
  expect(result.topGameByTime?.title).toBe("Hades PS5");
});

test("game connections affect gameWithLongestSession representative", async () => {
  const sessions = [
    s(1, "Elden Ring PC", "2024-01-01T10:00:00Z", "2024-01-01T16:00:00Z"),
    s(2, "Elden Ring PS5", "2024-01-02T10:00:00Z", "2024-01-02T12:00:00Z"),
    s(3, "Unrelated", "2024-01-03T10:00:00Z", "2024-01-03T14:00:00Z"),
  ];
  const edges: Edge[] = [{ game_id: 1, connected_game_id: 2 }];

  const result = await recap(sessions, { edges });

  expect(result.gameWithLongestSession?.title).toBe("Elden Ring PC");
  expect(result.gameWithLongestSession?.sessionSeconds).toBe(21600);
  expect(result.uniqueGames).toBe(2);
});

test("most binged game finds max sessions in 24h window", async () => {
  const sessions = [
    s(1, "Binged", "2024-01-05T08:00:00Z", "2024-01-05T09:00:00Z"),
    s(1, "Binged", "2024-01-05T12:00:00Z", "2024-01-05T13:00:00Z"),
    s(1, "Binged", "2024-01-05T18:00:00Z", "2024-01-05T19:00:00Z"),
    s(1, "Binged", "2024-01-05T22:00:00Z", "2024-01-05T23:00:00Z"),
    s(2, "Chill", "2024-01-10T10:00:00Z", "2024-01-10T11:00:00Z"),
    s(2, "Chill", "2024-01-17T10:00:00Z", "2024-01-17T11:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.mostBingedGame?.title).toBe("Binged");
  expect(result.mostBingedGame?.sessionCount).toBe(4);
});

test("binge detection merges connected game sessions", async () => {
  const sessions = [
    s(1, "Game A", "2024-01-01T08:00:00Z", "2024-01-01T09:00:00Z"),
    s(1, "Game A", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"),
    s(2, "Game A PS5", "2024-01-01T14:00:00Z", "2024-01-01T15:00:00Z"),
    s(2, "Game A PS5", "2024-01-01T18:00:00Z", "2024-01-01T19:00:00Z"),
    s(2, "Game A PS5", "2024-01-01T22:00:00Z", "2024-01-01T23:00:00Z"),
    s(3, "Other", "2024-01-02T08:00:00Z", "2024-01-02T09:00:00Z"),
    s(3, "Other", "2024-01-02T12:00:00Z", "2024-01-02T13:00:00Z"),
    s(3, "Other", "2024-01-02T18:00:00Z", "2024-01-02T19:00:00Z"),
  ];
  const edges: Edge[] = [{ game_id: 1, connected_game_id: 2 }];

  const result = await recap(sessions, { edges });

  expect(result.mostBingedGame?.sessionCount).toBe(5);
});

test("abandoned games counts games with exactly one session", async () => {
  const sessions = [
    s(1, "Tried Once", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z"),
    s(2, "Played Often", "2024-01-02T10:00:00Z", "2024-01-02T11:00:00Z"),
    s(2, "Played Often", "2024-01-03T10:00:00Z", "2024-01-03T11:00:00Z"),
    s(3, "Also Once", "2024-01-04T10:00:00Z", "2024-01-04T11:00:00Z"),
  ];

  const result = await recap(sessions);
  expect(result.abandonedGames).toBe(2);
});

test("return rate counts games played across multiple months", async () => {
  const range = {
    from: "2024-01-01",
    to: "2024-03-31",
    startedAt: new Date("2024-01-01T00:00:00Z"),
    endedAt: new Date("2024-04-01T00:00:00Z"),
  };

  const sessions = [
    s(1, "Returner", "2024-01-15T10:00:00Z", "2024-01-15T11:00:00Z"),
    s(1, "Returner", "2024-02-15T10:00:00Z", "2024-02-15T11:00:00Z"),
    s(2, "One Timer", "2024-01-20T10:00:00Z", "2024-01-20T11:00:00Z"),
  ];

  const result = await recap(sessions, { range });
  expect(result.returnRateGames).toBe(1);
});

test("new games tried counts first-ever sessions within range", async () => {
  const sessions = [
    s(1, "Old Fav", "2024-01-10T10:00:00Z", "2024-01-10T11:00:00Z"),
    s(2, "New Find", "2024-01-20T10:00:00Z", "2024-01-20T11:00:00Z"),
  ];
  const firstSessions = [
    fs(1, "Old Fav", "2023-06-15T10:00:00Z"),
    fs(2, "New Find", "2024-01-20T10:00:00Z"),
  ];

  const result = await recap(sessions, { firstSessions });
  expect(result.newGamesTried).toBe(1);
});

test("new games tried merges connected games by earliest first session", async () => {
  const sessions = [
    s(1, "A Steam", "2024-01-10T10:00:00Z", "2024-01-10T11:00:00Z"),
    s(2, "A PS5", "2024-01-15T10:00:00Z", "2024-01-15T11:00:00Z"),
  ];
  const firstSessions = [
    fs(1, "A Steam", "2023-06-01T10:00:00Z"),
    fs(2, "A PS5", "2024-01-15T10:00:00Z"),
  ];
  const edges: Edge[] = [{ game_id: 1, connected_game_id: 2 }];

  const result = await recap(sessions, { firstSessions, edges });
  expect(result.newGamesTried).toBe(0);
});

test("time on top 3 percent is calculated correctly", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T00:00:00Z", "2024-01-01T00:01:40Z"), // 100s
    s(2, "B", "2024-01-02T00:00:00Z", "2024-01-02T00:00:50Z"), // 50s
    s(3, "C", "2024-01-03T00:00:00Z", "2024-01-03T00:00:30Z"), // 30s
    s(4, "D", "2024-01-04T00:00:00Z", "2024-01-04T00:00:20Z"), // 20s
  ];

  const result = await recap(sessions);
  // Total=200s. Top 3=100+50+30=180s. 180/200*100=90
  expect(result.timeOnTop3Percent).toBe(90);
});

test("most used platform picks by total seconds", async () => {
  const sessions = [
    s(1, "A", "2024-01-01T10:00:00Z", "2024-01-01T13:00:00Z", "steam"),
    s(2, "B", "2024-01-02T10:00:00Z", "2024-01-02T15:00:00Z", "psn"),
    s(3, "C", "2024-01-03T10:00:00Z", "2024-01-03T11:00:00Z", "steam"),
  ];

  const result = await recap(sessions);
  expect(result.mostUsedPlatform?.platform).toBe("psn");
  expect(result.mostUsedPlatform?.totalSeconds).toBe(18000);
  expect(result.platformTotals.length).toBe(2);
});

test("topGamesByTime is limited to 5, gamesTable includes all", async () => {
  const sessions = Array.from({ length: 7 }, (_, i) =>
    s(
      i + 1,
      `Game ${i + 1}`,
      `2024-01-${String(i + 1).padStart(2, "0")}T10:00:00Z`,
      `2024-01-${String(i + 1).padStart(2, "0")}T11:00:00Z`
    )
  );

  const result = await recap(sessions);
  expect(result.topGamesByTime.length).toBe(5);
  expect(result.gamesTable.length).toBe(7);
});

test("averages per day, week, and month are computed from range", async () => {
  // Jan 1 - Jan 31 = 31 days, 1 month
  const sessions = Array.from({ length: 6 }, (_, i) =>
    s(
      1,
      "A",
      `2024-01-${String(i * 5 + 1).padStart(2, "0")}T10:00:00Z`,
      `2024-01-${String(i * 5 + 1).padStart(2, "0")}T11:00:00Z`
    )
  );

  const result = await recap(sessions);

  expect(result.averageSessionsPerDay).toBeCloseTo(6 / 31, 5);
  expect(result.averageSessionsPerWeek).toBeCloseTo(6 / (31 / 7), 5);
  expect(result.averageSessionsPerMonth).toBe(6);
});

test("manual sessions inherit mapped platform in aggregation", async () => {
  const sessions = [
    s(1, "Hades", "2024-01-01T10:00:00Z", "2024-01-01T11:00:00Z", "manual"),
    s(2, "Other", "2024-01-02T10:00:00Z", "2024-01-02T12:00:00Z", "steam"),
  ];
  const mappings: Mapping[] = [
    {
      canonical_title: "hades",
      mapped_game_id: 10,
      mapped_title: "Hades",
      mapped_platform: "steam",
      mapped_capsule_url: null,
      mapped_canonical_key: "steam:123",
    },
  ];

  const result = await recap(sessions, { mappings });

  const steamPlatform = result.platformTotals.find(
    (p) => p.platform === "steam"
  );
  const manualPlatform = result.platformTotals.find(
    (p) => p.platform === "manual"
  );
  expect(steamPlatform?.sessionCount).toBe(2);
  expect(manualPlatform).toBeUndefined();
});
