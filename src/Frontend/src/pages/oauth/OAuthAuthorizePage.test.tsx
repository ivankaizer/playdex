import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { MemoryRouter } from "react-router";
import { ThemeProvider } from "@/lib/theme";
import { OAuthAuthorizePage, readAuthorizeRequest } from "./OAuthAuthorizePage";

const query =
  "?response_type=code&client_id=https%3A%2F%2Fclaude.ai%2Foauth%2Fclient&redirect_uri=https%3A%2F%2Fclaude.ai%2Fapi%2Fmcp%2Fauth_callback" +
  "&code_challenge=abc&code_challenge_method=S256&state=s1&scope=drafts&resource=https%3A%2F%2Fdash.playdex.dev%2Fapi%2Fmcp";

const consent = {
  clientId: "https://claude.ai/oauth/client",
  clientName: "Claude",
  clientUri: "https://claude.ai",
  redirectHost: "claude.ai",
  loopbackOnly: false,
  resourceName: "Playdex for assistants",
  scopes: [{ name: "drafts", description: "Draft play sessions and log them once you accept them." }],
};

function jsonResponse(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function memoryStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    clear: () => items.clear(),
    getItem: (key) => items.get(key) ?? null,
    key: (index) => [...items.keys()][index] ?? null,
    removeItem: (key) => void items.delete(key),
    setItem: (key, value) => void items.set(key, String(value)),
  };
}

function renderPage(leave: (url: string) => void) {
  return render(
    <MemoryRouter initialEntries={[`/oauth/authorize${query}`]}>
      <ThemeProvider>
        <OAuthAuthorizePage leave={leave} />
      </ThemeProvider>
    </MemoryRouter>,
  );
}

function bodyOf(call: unknown[]): unknown {
  return JSON.parse(String((call[1] as RequestInit).body));
}

describe("OAuthAuthorizePage", () => {
  beforeEach(() => vi.stubGlobal("localStorage", memoryStorage()));
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("reads the authorization request from the query string", () => {
    expect(readAuthorizeRequest(query)).toEqual({
      responseType: "code",
      clientId: "https://claude.ai/oauth/client",
      redirectUri: "https://claude.ai/api/mcp/auth_callback",
      codeChallenge: "abc",
      codeChallengeMethod: "S256",
      state: "s1",
      scope: "drafts",
      resource: "https://dash.playdex.dev/api/mcp",
    });
  });

  it("shows the app and returns to it with the code once allowed", async () => {
    localStorage.setItem("playdex.token", "token");
    localStorage.setItem("playdex.email", "jo@example.com");
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, { data: { consent, redirectUrl: null }, error: null }))
      .mockResolvedValueOnce(jsonResponse(200, { data: { redirectUrl: "https://claude.ai/api/mcp/auth_callback?code=c1" }, error: null }));
    const leave = vi.fn();

    renderPage(leave);

    expect(await screen.findByRole("heading", { name: "Claude wants to use your Playdex" })).toBeInTheDocument();
    expect(screen.getByText("claude.ai")).toBeInTheDocument();
    expect(screen.getByText("jo@example.com")).toBeInTheDocument();
    expect(screen.getByText("Through Playdex for assistants, it will be able to:")).toBeInTheDocument();
    expect(screen.getByText("Draft play sessions and log them once you accept them.")).toBeInTheDocument();
    expect(screen.queryByRole("note")).not.toBeInTheDocument();
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/oauth\/authorize\/check$/);
    expect(bodyOf(fetchMock.mock.calls[0])).toMatchObject({ clientId: consent.clientId, state: "s1" });

    fireEvent.click(screen.getByRole("button", { name: "Allow" }));

    await waitFor(() => expect(leave).toHaveBeenCalledWith("https://claude.ai/api/mcp/auth_callback?code=c1"));
    expect(bodyOf(fetchMock.mock.calls[1])).toMatchObject({ approve: true, request: { codeChallenge: "abc" } });
  });

  it("sends a cancel back to the app as a decision", async () => {
    localStorage.setItem("playdex.token", "token");
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(jsonResponse(200, { data: { consent: { ...consent, loopbackOnly: true }, redirectUrl: null }, error: null }))
      .mockResolvedValueOnce(jsonResponse(200, { data: { redirectUrl: "http://127.0.0.1:4000/cb?error=access_denied" }, error: null }));
    const leave = vi.fn();

    renderPage(leave);

    expect(await screen.findByRole("note")).toHaveTextContent("runs on your own computer");
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    await waitFor(() => expect(leave).toHaveBeenCalledWith("http://127.0.0.1:4000/cb?error=access_denied"));
    expect(bodyOf(fetchMock.mock.calls[1])).toMatchObject({ approve: false });
  });

  it("returns at once when the request is invalid for the app", async () => {
    localStorage.setItem("playdex.token", "token");
    vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(
      jsonResponse(200, {
        data: { consent: null, redirectUrl: "https://claude.ai/api/mcp/auth_callback?error=invalid_request" },
        error: null,
      }),
    );
    const leave = vi.fn();

    renderPage(leave);

    await waitFor(() => expect(leave).toHaveBeenCalledWith("https://claude.ai/api/mcp/auth_callback?error=invalid_request"));
  });

  it("keeps an unknown app or redirect on the page", async () => {
    localStorage.setItem("playdex.token", "token");
    vi.spyOn(globalThis, "fetch").mockResolvedValueOnce(
      jsonResponse(400, { data: null, error: { error: "Claude did not register this redirect URI." } }),
    );
    const leave = vi.fn();

    renderPage(leave);

    expect(await screen.findByRole("alert")).toHaveTextContent("did not register this redirect URI");
    expect(leave).not.toHaveBeenCalled();
  });

  it("signs in first, then asks", async () => {
    const fetchMock = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        jsonResponse(200, { data: { userId: 1, email: "jo@example.com", accessToken: "a", refreshToken: "r" }, error: null }),
      )
      .mockResolvedValueOnce(jsonResponse(200, { data: { consent, redirectUrl: null }, error: null }));

    renderPage(vi.fn());

    fireEvent.change(screen.getByLabelText("Email"), { target: { value: "jo@example.com" } });
    fireEvent.change(screen.getByLabelText("Password"), { target: { value: "secret" } });
    fireEvent.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("heading", { name: "Claude wants to use your Playdex" })).toBeInTheDocument();
    expect(String(fetchMock.mock.calls[0][0])).toMatch(/\/login$/);
    expect(localStorage.getItem("playdex.token")).toBe("a");
  });
});
