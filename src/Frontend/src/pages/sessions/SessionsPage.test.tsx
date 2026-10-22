import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { SessionCreatePage } from "./SessionCreatePage";
import { SessionsPage } from "./SessionsPage";

function json(data: unknown) {
  return new Response(JSON.stringify({ data, error: null }), { status: 200, headers: { "Content-Type": "application/json" } });
}

const finished = {
  sessionId: 7,
  startedAt: "2026-07-13T10:00:00Z",
  endedAt: "2026-07-13T11:05:00Z",
  gameId: "42",
  platformKey: "steam",
  platformName: "Steam",
  game: "Hades II",
  capsuleUrl: null,
  ignored: true,
};

const stats = {
  totalSeconds: 3900,
  sessionCount: 1,
  topGameTitle: "Hades II",
  platforms: [{ platformKey: "steam", platformName: "Steam", totalSeconds: 3900 }],
  topGames: [{ gameId: "42", title: "Hades II", totalSeconds: 3900 }],
  months: [],
};

function Location() {
  const location = useLocation();
  return <output data-testid="location">{location.pathname + location.search}</output>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <ThemeProvider>
        <Routes>
          <Route path="/sessions" element={<SessionsPage />} />
          <Route path="/sessions/create" element={<SessionCreatePage />} />
        </Routes>
        <Location />
      </ThemeProvider>
    </MemoryRouter>,
  );
}

describe("SessionsPage", () => {
  let calls: Array<{ method: string; path: string; body?: unknown }>;

  beforeEach(() => {
    calls = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(input));
        const path = url.pathname.replace(/^\/api/, "") + url.search;
        const method = init?.method ?? "GET";
        calls.push({ method, path, body: typeof init?.body === "string" ? JSON.parse(init.body) : undefined });
        if (url.pathname.endsWith("/manual/platforms")) return json({ platforms: [{ key: "pc", name: "PC" }] });
        if (url.pathname.endsWith("/manual/sessions/current")) return json({ session: null });
        if (url.pathname.endsWith("/stats")) return json({ accountId: 1, stats });
        if (method === "DELETE") return new Response(null, { status: 204 });
        if (method === "PUT") return json({ sessionId: 7, startedAt: "", endedAt: "" });
        return json({ accountId: 1, sessions: [finished], total: 1, limit: 10, offset: 0 });
      }),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("loads list, stats, active session and platforms and renders rows grouped by day", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1 of 1 session"));
    const paths = calls.map((call) => call.path);
    expect(paths.some((path) => /^\/sessions\?limit=10&offset=0&from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}&sort=date&direction=desc$/.test(path))).toBe(true);
    expect(paths.some((path) => path.startsWith("/stats?from="))).toBe(true);
    expect(paths).toContain("/manual/sessions/current");
    expect(paths).toContain("/manual/platforms");
    // The duration and ignored badge render once in the desktop column and once in the compact (<=600px)
    // meta line; CSS shows exactly one of them.
    const row = container.querySelector("[data-session-row]")!;
    expect(row.querySelectorAll(".max-\\[600px\\]\\:hidden")).not.toHaveLength(0);
    expect(screen.getAllByText("1h 05m")).toHaveLength(2);
    expect(screen.getByText("1 session · 1h 05m")).toBeInTheDocument();
    expect(screen.getAllByLabelText("Ignored from stats")).toHaveLength(2);
    expect(container.querySelectorAll("button[data-action=edit]")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Actions for Hades II" })).toHaveAttribute("aria-haspopup", "menu");
    expect(row.querySelector("button[data-row-index]")).toHaveAttribute("title", "Hades II");
    expect(container.querySelector("button[data-action=load-more]")).toBeNull();
  });

  it("debounces the title filter and resets to the first page", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1 of 1 session"));
    vi.useFakeTimers();
    const before = calls.length;
    fireEvent.change(screen.getByPlaceholderText("Search by game title"), { target: { value: " Hades II " } });
    act(() => vi.advanceTimersByTime(299));
    expect(calls.length).toBe(before);
    await act(async () => vi.advanceTimersByTime(1));
    vi.useRealTimers();
    expect(calls.at(-1)?.path).toContain("&title=Hades+II&");
  });

  it("removal is confirm gated", async () => {
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1 of 1 session"));
    fireEvent.click(container.querySelector("button[data-action=remove]")!);
    expect(confirm).toHaveBeenCalledWith("Remove this session entry?");
    expect(calls.some((call) => call.method === "DELETE")).toBe(false);

    confirm.mockReturnValue(true);
    fireEvent.click(container.querySelector("button[data-action=remove]")!);
    await waitFor(() => expect(calls.some((call) => call.method === "DELETE" && call.path === "/sessions/7")).toBe(true));
    confirm.mockRestore();
  });

  it("edits a finished session through the overlay modal", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1 of 1 session"));
    fireEvent.click(container.querySelector("button[data-action=edit]")!);
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveTextContent("Hades II");
    fireEvent.click(screen.getByText("Save session"));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    const put = calls.find((call) => call.method === "PUT");
    expect(put?.path).toBe("/sessions/7");
    expect(new Date((put?.body as { startedAt: string }).startedAt).toISOString()).toBe("2026-07-13T10:00:00.000Z");
  });

  it("loads more rows under the list instead of numbered pages", async () => {
    const many = (from: number, count: number) =>
      Array.from({ length: count }, (_, index) => ({ ...finished, sessionId: from + index, game: `Game ${from + index}` }));
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
        const url = new URL(String(input));
        const path = url.pathname.replace(/^\/api/, "") + url.search;
        calls.push({ method: init?.method ?? "GET", path });
        if (url.pathname.endsWith("/manual/platforms")) return json({ platforms: [] });
        if (url.pathname.endsWith("/manual/sessions/current")) return json({ session: null });
        if (url.pathname.endsWith("/stats/daily")) return json({ accountId: 1, days: [] });
        if (url.pathname.endsWith("/stats")) return json({ accountId: 1, stats });
        const offset = Number(url.searchParams.get("offset"));
        const limit = Number(url.searchParams.get("limit"));
        return json({ accountId: 1, sessions: many(offset + 1, Math.min(limit, 13 - offset)), total: 13, limit, offset });
      }),
    );
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 10 of 13 sessions"));
    expect(container.querySelector("nav[aria-label=Pagination]")).toBeNull();
    const more = screen.getByRole("button", { name: "Load 3 more" });
    fireEvent.click(more);
    await waitFor(() => expect(container.innerHTML).toContain("Showing 13 of 13 sessions"));
    expect(calls.some((call) => call.path.startsWith("/sessions?limit=10&offset=10&"))).toBe(true);
    expect(container.querySelectorAll("[data-session-row]")).toHaveLength(13);
    expect(screen.queryByRole("button", { name: /Load \d+ more/ })).toBeNull();
    // Focus moves to the first newly loaded row once loading has finished (an effect after the rows render).
    await waitFor(() => expect(container.querySelector("[data-row-index='10']")).toHaveFocus());

    // A delete refreshes every loaded row, not just the first page.
    vi.spyOn(window, "confirm").mockReturnValue(true);
    fireEvent.click(container.querySelector("button[data-action=remove]")!);
    await waitFor(() => expect(calls.some((call) => call.path.startsWith("/sessions?limit=13&offset=0&"))).toBe(true));
    vi.mocked(window.confirm).mockRestore();
  });

  it("an empty range hides the charts and offers to log a session", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = new URL(String(input));
        if (url.pathname.endsWith("/manual/platforms")) return json({ platforms: [] });
        if (url.pathname.endsWith("/manual/sessions/current")) return json({ session: null });
        if (url.pathname.endsWith("/stats/daily")) return json({ accountId: 1, days: [] });
        if (url.pathname.endsWith("/stats")) {
          return json({ accountId: 1, stats: { totalSeconds: 0, sessionCount: 0, topGameTitle: null, platforms: [], topGames: [], months: [] } });
        }
        return json({ accountId: 1, sessions: [], total: 0, limit: 10, offset: 0 });
      }),
    );
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(screen.getByText("No sessions in this range")).toBeInTheDocument());
    expect(screen.getByText("Pick another range, or log a session you played offline.")).toBeInTheDocument();
    expect(container.querySelector("[data-session-history-row] [data-session-action=extended]")).not.toBeNull();
    expect(screen.queryByText("Platforms")).toBeNull();
    expect(screen.queryByText("Hours per day")).toBeNull();
    expect(container.innerHTML).not.toContain("Showing");
  });

  it("opens the session creator", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1 of 1 session"));
    fireEvent.click(container.querySelector("[data-session-action=extended]")!);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/sessions/create"));
  });
});

describe("SessionCreatePage", () => {
  beforeEach(() => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async (input: RequestInfo | URL) => {
        const url = new URL(String(input));
        if (url.pathname.endsWith("/manual/platforms")) return json({ platforms: [{ key: "pc", name: "PC" }] });
        if (url.pathname.endsWith("/manual/draft-capabilities")) {
          return json({ available: false, provider: "OpenRouter", maxTextCharacters: 1, maxImageBytes: 1, maxImagePixels: 1, maxRows: 1 });
        }
        if (url.pathname.endsWith("/manual/draft-batches")) return json([]);
        if (url.pathname.endsWith("/manual/sessions/current")) return json({ session: null });
        if (url.pathname.endsWith("/stats")) return json({ accountId: 1, stats });
        if (url.pathname.endsWith("/sessions")) return json({ accountId: 1, sessions: [], total: 0, limit: 10, offset: 0 });
        return json({ query: "", igdbAvailable: true, results: [] });
      }),
    );
  });

  afterEach(() => vi.unstubAllGlobals());

  it("toggles between the manual creator and the AI workspace", async () => {
    const { container } = renderAt("/sessions/create");
    expect(container.querySelector("[data-layout=extended-manual]")).not.toBeNull();
    expect(container.querySelector("input[type=date]")).not.toBeNull();
    const manual = container.querySelector("[data-mode-tab=manual]")!;
    const ai = container.querySelector("[data-mode-tab=ai]")!;
    expect(manual).toHaveAttribute("aria-pressed", "true");
    fireEvent.click(ai);
    expect(ai).toHaveAttribute("aria-pressed", "true");
    expect(container.querySelector("[data-layout=extended-ai]")).not.toBeNull();
    await waitFor(() => expect(container.innerHTML).toContain("Nothing waiting for review"));
    fireEvent.click(manual);
    expect(container.querySelector("[data-layout=extended-manual]")).not.toBeNull();
  });

  it("validates before saving and the breadcrumb returns to sessions", async () => {
    const { container } = renderAt("/sessions/create?mode=live");
    expect(container.querySelector("input[type=date]")).toBeNull();
    expect(container.querySelector("button[data-action=save]")).toBeDisabled();
    expect(container).toHaveTextContent("Choose a game to continue.");
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" });
    fireEvent.click(breadcrumb.querySelector("button")!);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/sessions"));
  });
});
