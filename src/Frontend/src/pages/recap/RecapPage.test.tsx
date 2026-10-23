import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { clientTimeZone } from "@/lib/dates";
import { ThemeProvider } from "@/lib/theme";
import { RecapPage } from "./RecapPage";

function json(data: unknown) {
  return new Response(JSON.stringify({ data, error: null }), { status: 200, headers: { "Content-Type": "application/json" } });
}

const tz = `tz=${encodeURIComponent(clientTimeZone())}`;

describe("RecapPage", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("loads years then the all-time recap and renders its sections", async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        calls.push(new URL(url).pathname.replace(/^\/api/, "") + new URL(url).search);
        if (new URL(url).pathname.endsWith("/recap/years")) return json({ years: [2026, 2025] });
        return json({
          range: { from: null, to: null },
          totalSessions: 3,
          totalSeconds: 7200,
          averageSessionSeconds: 2400,
          medianSessionSeconds: 2400,
          longestSessionSeconds: 3600,
          shortestSessionSeconds: 600,
          averageSessionsPerDay: 1.25,
          averageSessionsPerWeek: 2,
          averageSessionsPerMonth: 3,
          mostActiveDay: { date: "2026-07-13", sessionCount: 2 },
          mostActiveMonth: { month: "2026-07", sessionCount: 3 },
          longestStreakDays: 2,
          daysPlayed: 1,
          daysNotPlayed: 2,
          sessionsByWeekday: [{ weekday: 1, sessionCount: 1 }],
          sessionsByHour: [{ hour: 7, sessionCount: 1 }],
          peakHour: { hour: 7, sessionCount: 1 },
          weekendSeconds: 3600,
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
          platformTotals: [{ platformKey: "steam", platformName: "Steam", sessionCount: 3, totalSeconds: 7200 }],
          mostUsedPlatform: null,
          gamesTable: [
            {
              gameId: "42",
              title: "Halo",
              capsuleUrl: null,
              totalSeconds: 7200,
              sessionCount: 3,
              averageSessionSeconds: 2400,
              isAbandoned: true,
            },
          ],
        });
      }),
    );

    render(
      <MemoryRouter initialEntries={["/recap"]}>
        <ThemeProvider>
          <RecapPage />
        </ThemeProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText("Summed playtime by game")).toBeInTheDocument();
    // The layout's background role check (/me) is not part of the page's data flow.
    expect(calls.filter((path) => path !== "/me")).toEqual([`/recap/years?${tz}`, `/recap?${tz}`]);
    expect(screen.getByRole("button", { name: "2026" })).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "33% of days played" })).toBeInTheDocument();
    expect(screen.getByText(/across 3 sessions and 1 game$/)).toBeInTheDocument();
    expect(screen.getByText("Jul 2026 · 3")).toBeInTheDocument();
    expect(screen.getByText("1.3")).toBeInTheDocument();
    expect(screen.getByText("abandoned")).toBeInTheDocument();
    expect(screen.getByText("weekend · 1h")).toBeInTheDocument();
    expect(screen.getByText("weekday · 1h")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText("Loading recap...")).not.toBeInTheDocument());
  });

  function recapFixture(overrides: Record<string, unknown> = {}) {
    return {
      range: { from: null, to: null },
      totalSessions: 0,
      totalSeconds: 0,
      averageSessionSeconds: 0,
      medianSessionSeconds: 0,
      longestSessionSeconds: 0,
      shortestSessionSeconds: 0,
      averageSessionsPerDay: 0,
      averageSessionsPerWeek: 0,
      averageSessionsPerMonth: 0,
      mostActiveDay: null,
      mostActiveMonth: null,
      longestStreakDays: 0,
      daysPlayed: 0,
      daysNotPlayed: 0,
      sessionsByWeekday: [],
      sessionsByHour: [],
      peakHour: null,
      weekendSeconds: 0,
      weekdaySeconds: 0,
      uniqueGames: 0,
      topGameBySessions: null,
      topGameByTime: null,
      topGamesByTime: [],
      topGameByAverageSession: null,
      gameWithLongestSession: null,
      newGamesTried: 0,
      abandonedGames: 0,
      returnRateGames: 0,
      topThreeGamesSharePercent: 0,
      mostBingedGame: null,
      platformTotals: [],
      mostUsedPlatform: null,
      gamesTable: [],
      ...overrides,
    };
  }

  function renderWith(recap: unknown) {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) =>
        new URL(String(input)).pathname.endsWith("/recap/years") ? json({ years: [] }) : json(recap),
      ),
    );
    render(
      <MemoryRouter initialEntries={["/recap"]}>
        <ThemeProvider>
          <RecapPage />
        </ThemeProvider>
      </MemoryRouter>,
    );
  }

  it("shows one empty state for a brand-new account instead of empty sections", async () => {
    renderWith(recapFixture());
    expect(await screen.findByText("Nothing to recap yet")).toBeInTheDocument();
    expect(screen.queryByText("Summed playtime by game")).not.toBeInTheDocument();
    expect(screen.queryByText(/NaN/)).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to sessions" })).toBeInTheDocument();
  });

  it("formats worst-case figures and shows the first 20 library rows with a Show all toggle", async () => {
    const long = "Warhammer 40,000: Dawn of War – Definitive Edition";
    const game = (index: number, seconds: number) => ({
      gameId: String(index),
      title: index === 0 ? long : `Game ${index}`,
      capsuleUrl: null,
      totalSeconds: seconds,
      sessionCount: index === 0 ? 1284000 : 1,
      averageSessionSeconds: 60,
      isAbandoned: false,
    });
    const table = Array.from({ length: 25 }, (_, index) => game(index, index === 0 ? 173568960 : 100));
    renderWith(
      recapFixture({
        totalSessions: 1284000,
        totalSeconds: 173568960,
        uniqueGames: 25,
        longestStreakDays: 1,
        daysPlayed: 1,
        daysNotPlayed: 999,
        topGameByTime: table[0],
        topGamesByTime: [table[0], game(99, 0)],
        platformTotals: [
          { platformKey: "steam", platformName: "Steam", sessionCount: 1284000, totalSeconds: 173568960 },
          { platformKey: "psn", platformName: "PSN", sessionCount: 1, totalSeconds: 100 },
          { platformKey: "pc", platformName: "PC", sessionCount: 0, totalSeconds: 0 },
        ],
        gamesTable: table,
      }),
    );

    expect(await screen.findByText("Summed playtime by game")).toBeInTheDocument();
    expect(screen.getAllByText("48,213.6h").length).toBeGreaterThan(0);
    expect(screen.getByText(/across 1,284,000 sessions and 25 games$/)).toBeInTheDocument();
    // Only games with playtime rank, and the heading counts them.
    expect(screen.getByText("Top game")).toBeInTheDocument();
    expect(screen.getAllByTitle(long).length).toBeGreaterThan(0);
    expect(screen.getAllByText("1 day")).toHaveLength(2);
    expect(screen.getByRole("img", { name: "<1% of days played" })).toBeInTheDocument();
    // A zero-hour platform is left out; a sliver reads <1% and <0.1h.
    expect(screen.queryByText("PC")).not.toBeInTheDocument();
    expect(screen.getByText("<0.1h · 1 session")).toBeInTheDocument();

    expect(screen.getAllByRole("row")).toHaveLength(21);
    fireEvent.click(screen.getByRole("button", { name: "Show all 25 games" }));
    expect(screen.getAllByRole("row")).toHaveLength(26);
    fireEvent.click(screen.getByRole("button", { name: "Show top 20" }));
    expect(screen.getAllByRole("row")).toHaveLength(21);
  });
});
