import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authSession } from "@/lib/auth";
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
  failedGenerations: [
    { id: "g-1", email: "player@example.test", model: "m", status: "timeout", createdAt: "2026-09-16T11:00:00Z" },
  ],
});

const fetchMock = vi.fn<typeof fetch>();

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/*" element={<AdminApp />} />
      </Routes>
    </MemoryRouter>,
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
});
