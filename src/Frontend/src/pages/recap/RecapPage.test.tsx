import { render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { RecapPage } from "./RecapPage";

function json(data: unknown) {
  return new Response(JSON.stringify({ data, error: null }), { status: 200, headers: { "Content-Type": "application/json" } });
}

describe("RecapPage", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("loads years then the all-time recap and renders its sections", async () => {
    const calls: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = String(input);
        calls.push(new URL(url).pathname.replace(/^\/api/, "") + new URL(url).search);
        if (url.endsWith("/recap/years")) return json({ years: [2026, 2025] });
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
          timeOnTop3Percent: 100,
          mostBingedGame: null,
          platformTotals: [{ platformKey: "steam", platformName: "Steam", sessionCount: 3, totalSeconds: 7200, source: null }],
          mostUsedPlatform: null,
          gamesTable: [{ gameId: "42", title: "Halo", capsuleUrl: null, totalSeconds: 7200, sessionCount: 3, averageSessionSeconds: 2400, isAbandoned: true }],
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
    expect(calls.filter((path) => path !== "/me")).toEqual(["/recap/years", "/recap"]);
    expect(screen.getByRole("button", { name: "2026" })).toBeInTheDocument();
    expect(screen.getByText("1 / 3 days")).toBeInTheDocument();
    expect(screen.getByText("Jul 2026 · 3")).toBeInTheDocument();
    expect(screen.getByText("1.3")).toBeInTheDocument();
    expect(screen.getByText("abandoned")).toBeInTheDocument();
    expect(screen.getByText("weekend · 1h")).toBeInTheDocument();
    expect(screen.getByText("weekday · 1h")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText("Loading recap...")).not.toBeInTheDocument());
  });
});
