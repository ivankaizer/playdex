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
  mappedPlatform: null,
  mappedSource: null,
};

const stats = {
  totalSeconds: 3900,
  sessionCount: 1,
  topGameTitle: "Hades II",
  platforms: [{ platformKey: "steam", platformName: "Steam", totalSeconds: 3900, source: null }],
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

  it("loads list, stats, active session and platforms and renders rows in both layouts", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1-1 of 1 sessions"));
    const paths = calls.map((call) => call.path);
    expect(paths.some((path) => /^\/sessions\?limit=10&offset=0&from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}&sort=date&direction=desc$/.test(path))).toBe(true);
    expect(paths.some((path) => path.startsWith("/stats?from="))).toBe(true);
    expect(paths).toContain("/manual/sessions/current");
    expect(paths).toContain("/manual/platforms");
    expect(screen.getAllByText("1h 05m")).toHaveLength(2);
    expect(screen.getAllByLabelText("Ignored from stats")).toHaveLength(2);
    expect(container.querySelectorAll("button[data-action=edit]")).toHaveLength(2);
  });

  it("debounces the title filter and resets to the first page", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1-1 of 1 sessions"));
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
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1-1 of 1 sessions"));
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
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1-1 of 1 sessions"));
    fireEvent.click(container.querySelector("button[data-action=edit]")!);
    const dialog = screen.getByRole("dialog");
    expect(dialog).toHaveTextContent("Hades II");
    fireEvent.click(screen.getByText("Save changes"));
    await waitFor(() => expect(screen.queryByRole("dialog")).toBeNull());
    const put = calls.find((call) => call.method === "PUT");
    expect(put?.path).toBe("/sessions/7");
    expect(new Date((put?.body as { startedAt: string }).startedAt).toISOString()).toBe("2026-07-13T10:00:00.000Z");
  });

  it("opens the session creator", async () => {
    const { container } = renderAt("/sessions");
    await waitFor(() => expect(container.innerHTML).toContain("Showing 1-1 of 1 sessions"));
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
    const toggle = container.querySelector("[data-workspace-toggle]")!;
    expect(toggle).toHaveTextContent("AI batches");
    fireEvent.click(toggle);
    expect(toggle).toHaveTextContent("Manual session");
    expect(container.querySelector("[data-layout=extended-ai]")).not.toBeNull();
    await waitFor(() => expect(container.innerHTML).toContain("No saved drafts"));
    fireEvent.click(toggle);
    expect(container.querySelector("[data-layout=extended-manual]")).not.toBeNull();
  });

  it("validates before saving and the breadcrumb returns to sessions", async () => {
    const { container } = renderAt("/sessions/create?mode=live");
    expect(container.querySelector("input[type=date]")).toBeNull();
    fireEvent.click(container.querySelector("button[data-action=save]")!);
    expect(screen.getByRole("alert")).toHaveTextContent("Select a game.");
    const breadcrumb = screen.getByRole("navigation", { name: "Breadcrumb" });
    fireEvent.click(breadcrumb.querySelector("button")!);
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("/sessions"));
  });
});
