import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiBlob, apiRequest, revokeRefreshToken } from "./api";
import { authSession } from "./auth";

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const ok = (data: unknown) => json(200, { data, error: null });
const unauthorized = () => json(401, { data: null, error: { error: "Invalid token." } });
const tokens = (access: string, refresh: string) => ok({ accessToken: access, refreshToken: refresh });

const fetchMock = vi.fn<typeof fetch>();
const assign = vi.fn();

const isRefresh = (input: Parameters<typeof fetch>[0]) => String(input).endsWith("/api/refresh");
const refreshCalls = () => fetchMock.mock.calls.filter(([input]) => isRefresh(input));
const bearer = (init: RequestInit | undefined) => (init?.headers as Record<string, string> | undefined)?.Authorization;

/** An API where only `valid` is accepted as access token, and /refresh answers with `refresh()`. */
function api(valid: string, refresh: () => Promise<Response> | Response) {
  fetchMock.mockImplementation(async (input, init) => {
    if (isRefresh(input)) return refresh();
    return bearer(init) === `Bearer ${valid}` ? ok({ path: String(input) }) : unauthorized();
  });
}

beforeEach(() => {
  localStorage.clear();
  fetchMock.mockReset();
  assign.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  vi.stubGlobal("location", { ...window.location, assign });
  authSession.storeTokens("expired", "refresh-1");
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("token refresh", () => {
  it("shares one /refresh call between requests that fail together", async () => {
    api("fresh", () => tokens("fresh", "refresh-2"));

    const results = await Promise.all([apiRequest("/a"), apiRequest("/b"), apiRequest("/c")]);

    expect(results).toHaveLength(3);
    expect(refreshCalls()).toHaveLength(1);
    expect(JSON.parse(String(refreshCalls()[0]![1]?.body))).toEqual({ refreshToken: "refresh-1" });
    expect(authSession.getAccessToken()).toBe("fresh");
    expect(authSession.getRefreshToken()).toBe("refresh-2");
  });

  it("retries with a token another tab already got instead of refreshing again", async () => {
    let sent = 0;
    fetchMock.mockImplementation(async (input, init) => {
      if (isRefresh(input)) return tokens("never", "never");
      if (sent++ === 0) {
        authSession.storeTokens("from-other-tab", "refresh-2");
        return unauthorized();
      }
      return bearer(init) === "Bearer from-other-tab" ? ok({}) : unauthorized();
    });

    await expect(apiRequest("/a")).resolves.toEqual({});
    expect(refreshCalls()).toHaveLength(0);
  });

  it("takes turns with other tabs through a Web Lock", async () => {
    const request = vi.fn((_name: string, work: () => Promise<unknown>) => work());
    vi.stubGlobal("navigator", { ...navigator, locks: { request } });
    api("fresh", () => tokens("fresh", "refresh-2"));

    await apiRequest("/a");

    expect(request).toHaveBeenCalledOnce();
    expect(request.mock.calls[0]![0]).toBe("playdex.refresh");
  });

  it.each([401, 400])("signs out and returns home when /refresh answers %i", async (status) => {
    api("fresh", () => json(status, { data: null, error: { error: "Invalid token." } }));

    const error = await apiRequest("/a").catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect(authSession.getAccessToken()).toBeNull();
    expect(authSession.getRefreshToken()).toBeNull();
    expect(assign).toHaveBeenCalledWith("/");
  });

  it.each([
    ["the network is down", () => Promise.reject(new TypeError("Failed to fetch"))],
    ["the server fails", () => json(503, { data: null, error: { error: "Unavailable." } })],
    ["the server is busy", () => json(429, { data: null, error: { error: "Too many requests." } })],
    ["the answer is not JSON", () => new Response("<html>", { status: 200 })],
  ])("keeps the session when %s", async (_case, refresh) => {
    api("fresh", refresh);

    await expect(apiRequest("/a")).rejects.toBeDefined();

    expect(authSession.getAccessToken()).toBe("expired");
    expect(authSession.getRefreshToken()).toBe("refresh-1");
    expect(assign).not.toHaveBeenCalled();
  });

  it("reports why the session couldn't be renewed", async () => {
    api("fresh", () => json(503, { data: null, error: { error: "Unavailable." } }));

    await expect(apiRequest("/a")).rejects.toMatchObject({ status: 503, message: "Unavailable." });
  });

  it("runs a caller's own handler instead of the default", async () => {
    api("fresh", () => unauthorized());
    const onAuthFailure = vi.fn();

    await expect(apiRequest("/a", { onAuthFailure })).rejects.toBeInstanceOf(ApiError);

    expect(onAuthFailure).toHaveBeenCalledOnce();
    expect(assign).not.toHaveBeenCalled();
  });

  it("leaves an ended session to callers that opt out", async () => {
    api("fresh", () => unauthorized());

    await expect(apiRequest("/a", { onAuthFailure: null })).rejects.toMatchObject({ status: 401 });

    expect(authSession.getRefreshToken()).toBe("refresh-1");
    expect(assign).not.toHaveBeenCalled();
  });

  it("lets one caller stop waiting without cancelling the refresh for the others", async () => {
    let answer: (response: Response) => void = () => undefined;
    api("fresh", () => new Promise<Response>((resolve) => (answer = resolve)));
    const controller = new AbortController();

    const cancelled = apiRequest("/a", { signal: controller.signal });
    const waiting = apiRequest("/b");
    await vi.waitFor(() => expect(refreshCalls()).toHaveLength(1));
    controller.abort();
    answer(tokens("fresh", "refresh-2"));

    await expect(cancelled).rejects.toMatchObject({ name: "AbortError" });
    await expect(waiting).resolves.toEqual({ path: expect.stringMatching(/\/api\/b$/) });
  });

  it("applies to binary downloads too", async () => {
    api("fresh", () => unauthorized());

    await expect(apiBlob("/me/export")).rejects.toBeInstanceOf(ApiError);

    expect(assign).toHaveBeenCalledWith("/");
  });

  it("never refreshes for requests sent without a session", async () => {
    fetchMock.mockResolvedValue(unauthorized());

    await expect(apiRequest("/login", { method: "POST", body: {}, authenticated: false })).rejects.toBeInstanceOf(ApiError);

    expect(refreshCalls()).toHaveLength(0);
    expect(assign).not.toHaveBeenCalled();
  });
});

describe("revokeRefreshToken", () => {
  it("asks the API to revoke the stored refresh token", () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }));

    revokeRefreshToken();

    const [url, init] = fetchMock.mock.calls[0]!;
    expect(String(url)).toMatch(/\/api\/logout$/);
    expect(JSON.parse(String(init?.body))).toEqual({ refreshToken: "refresh-1" });
    expect(init?.keepalive).toBe(true);
  });

  it("does nothing without a session and never throws", () => {
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));
    revokeRefreshToken();
    authSession.logout();
    revokeRefreshToken();

    expect(fetchMock).toHaveBeenCalledOnce();
  });
});
