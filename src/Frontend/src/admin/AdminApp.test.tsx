import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authSession } from "@/lib/auth";
import { ThemeProvider } from "@/lib/theme";
import AdminApp from "./AdminApp";
import { NotAdministratorMessage } from "./auth";

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const ok = (data: unknown) => json(200, { data, error: null });

const snapshot = (updatedAt: string) => ({
  updatedAt,
  counts: { liveSessions: 2, liveUsers: 1, sessionsToday: 5, aiErrors: 3 },
  platforms: [{ key: "steam", name: "Steam" }],
  liveSessions: [
    {
      id: 1,
      email: "player@example.test",
      game: "Hades",
      capsuleUrl: null,
      platformKey: "steam",
      platform: "Steam",
      accountId: null,
      source: "polling",
      startedAt: "2026-09-16T10:00:00Z",
      endedAt: null,
      lastPolledAt: null,
    },
  ],
  endedSessions: [],
  failedGenerations: [{ id: "g-1", email: "player@example.test", model: "m", status: "timeout", createdAt: "2026-09-16T11:00:00Z" }],
});

const fetchMock = vi.fn<typeof fetch>();

function renderAt(path: string) {
  // main.tsx mounts the whole app, admin included, inside ThemeProvider (the admin header has a theme toggle).
  return render(
    <ThemeProvider>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/admin/*" element={<AdminApp />} />
        </Routes>
      </MemoryRouter>
    </ThemeProvider>,
  );
}

function calledPaths(): string[] {
  return fetchMock.mock.calls.map(([url]) => new URL(String(url)).pathname);
}

beforeEach(() => {
  localStorage.clear();
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe("AdminApp", () => {
  it("sends anonymous visitors to the login page", async () => {
    renderAt("/admin/games");
    expect(await screen.findByRole("heading", { name: "Admin login" })).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refuses signed-in players who are not administrators", async () => {
    authSession.storeTokens("access", "refresh");
    fetchMock.mockResolvedValueOnce(ok({ userId: 1, email: "p@example.test", isAdmin: false }));
    renderAt("/admin/sessions");
    expect(await screen.findByRole("heading", { name: "Admin login" })).toBeInTheDocument();
    expect(screen.getByRole("alert")).toHaveTextContent(NotAdministratorMessage);
  });

  it("signs an administrator in, shows the dashboard and logs out", async () => {
    const user = userEvent.setup();
    fetchMock.mockImplementation(async (input) => {
      const path = new URL(String(input)).pathname;
      if (path === "/api/login")
        return ok({ userId: 1, email: "ivan.kaizer@example.test", isAdmin: true, accessToken: "a", refreshToken: "r" });
      if (path === "/api/me") return ok({ userId: 1, email: "ivan.kaizer@example.test", isAdmin: true });
      if (path === "/api/admin/dashboard") return ok(snapshot("2026-09-16T12:00:00Z"));
      return json(404, { data: null, error: { error: "Route not found." } });
    });
    renderAt("/admin/login");
    await user.type(screen.getByLabelText("Email"), "ivan.kaizer@example.test");
    await user.type(screen.getByLabelText("Password"), "secret");
    await user.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("Users playing")).toBeInTheDocument();
    expect(screen.getByText("IK")).toBeInTheDocument();
    expect(screen.getByText("No poll recorded")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /AI errors · 24h/ })).toHaveAttribute(
      "href",
      expect.stringContaining("/admin/ai-generations?status=errors&since="),
    );
    expect(screen.getByRole("link", { name: /player@example.test/ })).toHaveAttribute("href", "/admin/ai-generations?id=g-1");
    const dashboardCall = fetchMock.mock.calls.find(([url]) => String(url).includes("/api/admin/dashboard"))!;
    const query = new URL(String(dashboardCall[0])).searchParams;
    expect(query.get("dayStart")).not.toBeNull();
    expect(query.get("dayEnd")).not.toBeNull();

    await user.click(screen.getByRole("button", { name: "Logout" }));
    expect(await screen.findByRole("heading", { name: "Admin login" })).toBeInTheDocument();
    expect(authSession.isLoggedIn()).toBe(false);
  });

  it("refreshes the dashboard every 30 seconds with the applied filters", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    authSession.storeTokens("access", "refresh");
    fetchMock.mockImplementation(async (input) => {
      const path = new URL(String(input)).pathname;
      if (path === "/api/me") return ok({ userId: 1, email: "a@b.test", isAdmin: true });
      return ok(snapshot(new Date().toISOString()));
    });
    renderAt("/admin");
    await screen.findByText("Users playing");
    const dashboardCalls = () => calledPaths().filter((path) => path === "/api/admin/dashboard").length;
    expect(dashboardCalls()).toBe(1);

    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    await user.type(screen.getByLabelText("Search live sessions"), "  hades ");
    await user.click(screen.getByRole("button", { name: "Search" }));
    await waitFor(() => expect(dashboardCalls()).toBe(2));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(30_000);
    });
    await waitFor(() => expect(dashboardCalls()).toBe(3));
    const last = fetchMock.mock.calls.filter(([url]) => String(url).includes("/api/admin/dashboard")).at(-1)!;
    expect(new URL(String(last[0])).searchParams.get("query")).toBe("hades");
  });

  it("deletes the selected sessions after a confirmation and reloads the page", async () => {
    const user = userEvent.setup();
    authSession.storeTokens("access", "refresh");
    const session = (id: number, game: string) => ({
      id,
      email: "player@example.test",
      platform: "Steam",
      accountId: "7656",
      game,
      startedAt: "2026-10-10T18:00:00Z",
      endedAt: "2026-10-10T19:00:00Z",
      live: false,
    });
    let rows = [session(1, "Hades II"), session(2, "Balatro")];
    const bodies: unknown[] = [];
    fetchMock.mockImplementation(async (input, init) => {
      const path = new URL(String(input)).pathname;
      if (path === "/api/me") return ok({ userId: 1, email: "a@b.test", isAdmin: true });
      if (path === "/api/admin/integrations") return ok([]);
      if (path === "/api/admin/sessions") return ok({ sessions: rows, total: rows.length, page: 0, pageSize: 25 });
      if (path === "/api/admin/sessions/delete") {
        const body = JSON.parse(String(init?.body)) as { sessionIds: number[] };
        bodies.push(body);
        rows = rows.filter((row) => !body.sessionIds.includes(row.id));
        return ok({ deleted: body.sessionIds.length });
      }
      return json(404, { data: null, error: { error: "Route not found." } });
    });
    const confirm = vi.fn(() => true);
    vi.stubGlobal("confirm", confirm);
    renderAt("/admin/sessions");

    const first = await screen.findByLabelText("Select session 1");
    expect(screen.getByRole("button", { name: "Delete selected" })).toBeDisabled();
    await user.click(first);
    await user.click(screen.getByRole("button", { name: "Delete selected (1)" }));

    expect(confirm).toHaveBeenCalledWith("Delete 1 selected session? This can't be undone.");
    expect(bodies).toEqual([{ sessionIds: [1] }]);
    expect(await screen.findByText("Deleted 1 session.")).toBeInTheDocument();
    await waitFor(() => expect(screen.queryByText("Hades II")).not.toBeInTheDocument());
    expect(screen.getByText("Balatro")).toBeInTheDocument();

    confirm.mockReturnValueOnce(false);
    await user.click(screen.getByLabelText("Select session 2"));
    await user.click(screen.getByRole("button", { name: "Delete selected (1)" }));
    expect(bodies).toHaveLength(1);
  });
});
