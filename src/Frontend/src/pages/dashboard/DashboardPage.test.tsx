import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { DashboardPage } from "./DashboardPage";

function json(data: unknown, status = 200) {
  return new Response(JSON.stringify(status === 200 ? { data, error: null } : data), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const stats = {
  totalSeconds: 3600,
  sessionCount: 1,
  topGameTitle: "Hades II",
  platforms: [{ platformKey: "steam", platformName: "Steam", totalSeconds: 3600, source: null }],
  topGames: [{ gameId: "42", title: "Hades II", totalSeconds: 3600 }],
  months: [],
};

describe("DashboardPage", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date("2026-07-13T12:00:00Z"));
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = new URL(String(input));
        const path = url.pathname.replace(/^\/api/, "");
        if (path === "/me") return json({ userId: 1, email: "player@example.test" });
        if (path === "/manual/platforms") return json({ platforms: [{ key: "pc", name: "PC" }] });
        if (path === "/sessions/current" || path === "/manual/sessions/current") return json({ session: null });
        if (path === "/stats/daily") return json({ days: [] });
        // The year's stats fail; everything else loads.
        if (path === "/stats" && url.searchParams.get("from") === "2026-01-01") {
          return json({ status: 500, detail: "Internal server error." }, 500);
        }
        if (path === "/stats") return json({ accountId: 1, stats });
        return json({ accountId: 1, sessions: [], total: 0, limit: 10, offset: 0 });
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("shows a failing panel's error and loads every other panel", async () => {
    render(
      <MemoryRouter initialEntries={["/dashboard"]}>
        <ThemeProvider>
          <DashboardPage />
        </ThemeProvider>
      </MemoryRouter>,
    );

    // Today and this month loaded.
    await waitFor(() => expect(screen.getByText("played across 1 session")).toBeInTheDocument());
    expect(screen.getByText("1h 0m")).toBeInTheDocument();
    expect(await screen.findByText("No sessions yet. Start one above, or log a past session from Sessions.")).toBeInTheDocument();
    expect(screen.getAllByText("Hades II").length).toBeGreaterThan(0);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    // The year's panel shows why it is empty.
    fireEvent.click(screen.getByRole("button", { name: "2026" }));
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Couldn't load this year's stats. Playdex ran into a problem on its side. Try again in a moment.",
    );
  });
});
