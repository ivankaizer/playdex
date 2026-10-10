import type { RecapGameStat, RecapResponse } from "./recapModels";
import { formatSessionsAverage, heroFacts, titleSizeClass, topGames, topGamesTitle } from "./recapView";

const game = (title: string, totalSeconds: number): RecapGameStat => ({
  gameId: title,
  title,
  capsuleUrl: null,
  totalSeconds,
  sessionCount: 1,
  averageSessionSeconds: 0,
  isAbandoned: false,
});

describe("recap view helpers", () => {
  it("ranks only games with playtime, longest first, at most five", () => {
    const recap = {
      topGamesByTime: [game("A", 10), game("B", 0), game("C", 30), game("D", 1), game("E", 2), game("F", 3), game("G", 4)],
    } as RecapResponse;
    expect(topGames(recap).map((item) => item.title)).toEqual(["C", "A", "G", "F", "E"]);
    expect(topGamesTitle(1)).toBe("Top game");
    expect(topGamesTitle(3)).toBe("Top 3 games");
  });

  it("steps the TOP TITLE font down for long names", () => {
    expect(titleSizeClass("Rez")).toBe("text-xl");
    expect(titleSizeClass("Clair Obscur: Expedition 33")).toContain("text-[17px]");
    expect(titleSizeClass("Warhammer 40,000: Dawn of War – Definitive Edition")).toContain("text-[15px]");
  });

  it("shows the streak for all time and the average session for a period", () => {
    const recap = {
      topGameByTime: null,
      mostActiveMonth: { month: "2026-07", sessionCount: 3 },
      longestStreakDays: 1284,
      averageSessionSeconds: 3660,
    } as unknown as RecapResponse;
    expect(heroFacts(recap, true)).toEqual([
      { label: "Top title", value: "—" },
      { label: "Most active month", value: "Jul 2026" },
      { label: "Longest streak", value: "1,284 days" },
    ]);
    expect(heroFacts(recap, false)[2]).toEqual({ label: "Avg. session", value: "1h 1m" });
  });

  it("groups session averages with one decimal", () => {
    expect(formatSessionsAverage(1000.04)).toBe("1,000");
    expect(formatSessionsAverage(38.12)).toBe("38.1");
    expect(formatSessionsAverage(Number.NaN)).toBe("0");
  });
});
