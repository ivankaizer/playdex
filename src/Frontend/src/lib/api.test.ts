import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiBaseUrl, apiBlob, apiRequest, buildUrl, errorMessage, isAbortError, revokeRefreshToken } from "./api";
import { authSession } from "./auth";
import { getConfig, setConfig } from "./config";

// Token refresh (what happens after a 401) has its own block at the end; the tests before it stay away from 401s.

const ApiBase = "https://api.playdex.test/api/";

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const fetchMock = vi.fn<typeof fetch>();
const originalConfig = getConfig();

function lastCall(): { url: string; init: RequestInit } {
  const [url, init] = fetchMock.mock.calls.at(-1)!;
  return { url: String(url), init: init ?? {} };
}

function headersOf(init: RequestInit): Record<string, string> {
  return (init.headers ?? {}) as Record<string, string>;
}

const ok = (data: unknown) => json(200, { data, error: null });
const unauthorized = () => json(401, { data: null, error: { error: "Invalid token." } });
const tokens = (access: string, refresh: string) => ok({ accessToken: access, refreshToken: refresh });

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
  vi.stubGlobal("fetch", fetchMock);
  setConfig({ apiBase: ApiBase, sentry: null });
});

afterEach(() => {
  vi.unstubAllGlobals();
  setConfig(originalConfig);
});

describe("buildUrl", () => {
  it.each([
    ["/sessions", "https://api.playdex.test/api/sessions"],
    ["sessions", "https://api.playdex.test/api/sessions"],
    ["//sessions?limit=10", "https://api.playdex.test/api/sessions?limit=10"],
    ["/games/abc/stats", "https://api.playdex.test/api/games/abc/stats"],
  ])("joins %s to the API base with one slash", (path, expected) => {
    expect(buildUrl(path)).toBe(expected);
  });

  it("accepts a base without a trailing slash or with several", () => {
    setConfig({ apiBase: " https://api.playdex.test/api ", sentry: null });
    expect(buildUrl("/me")).toBe("https://api.playdex.test/api/me");
    setConfig({ apiBase: "https://api.playdex.test/api///", sentry: null });
    expect(buildUrl("/me")).toBe("https://api.playdex.test/api/me");
  });

  it("leaves absolute URLs alone", () => {
    expect(buildUrl("https://images.test/cover.png")).toBe("https://images.test/cover.png");
    expect(buildUrl("HTTP://images.test/cover.png")).toBe("HTTP://images.test/cover.png");
  });
});

describe("apiBaseUrl", () => {
  it("resolves a relative base against the page and drops trailing slashes", () => {
    setConfig({ apiBase: "/api/", sentry: null });
    expect(apiBaseUrl()).toBe(`${window.location.origin}/api`);
  });

  it("keeps an absolute base", () => {
    expect(apiBaseUrl()).toBe("https://api.playdex.test/api");
  });
});

describe("apiRequest", () => {
  it("unwraps the API envelope", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { data: { sessions: [1, 2] }, error: null }));

    await expect(apiRequest<{ sessions: number[] }>("/sessions")).resolves.toEqual({ sessions: [1, 2] });
    expect(lastCall().url).toBe("https://api.playdex.test/api/sessions");
    expect(lastCall().init.method).toBe("GET");
  });

  it("returns payloads that are not enveloped as they are", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { dsn: null, environment: "production" }));
    await expect(apiRequest("/observability/sentry")).resolves.toEqual({ dsn: null, environment: "production" });

    fetchMock.mockResolvedValueOnce(json(200, [1, 2, 3]));
    await expect(apiRequest("/list")).resolves.toEqual([1, 2, 3]);
  });

  it("returns undefined for 204 No Content", async () => {
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }));
    await expect(apiRequest("/sessions/1", { method: "DELETE" })).resolves.toBeUndefined();
  });

  it("rejects a successful response whose envelope has no data", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { data: null, error: null }));
    await expect(apiRequest("/me")).rejects.toThrow(SyntaxError);
  });

  it("sends a JSON body with its content type", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { data: { ok: true }, error: null }));

    await apiRequest("/settings", { method: "PUT", body: { ignoreShortSessionsMinutes: 5 } });

    const { init } = lastCall();
    expect(init.method).toBe("PUT");
    expect(init.body).toBe('{"ignoreShortSessionsMinutes":5}');
    expect(headersOf(init)["Content-Type"]).toBe("application/json; charset=utf-8");
  });

  it("builds a fresh body from a factory and lets the browser set multipart headers", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { data: { id: "batch" }, error: null }));
    const form = new FormData();
    form.append("text", "Hades for an hour");
    const factory = vi.fn(() => form);

    await apiRequest("/manual/drafts", { method: "POST", body: factory });

    expect(factory).toHaveBeenCalledOnce();
    expect(lastCall().init.body).toBe(form);
    expect(headersOf(lastCall().init)["Content-Type"]).toBeUndefined();
  });

  it("sends no body when there is none", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { data: { ok: true }, error: null }));
    await apiRequest("/key", { method: "POST" });
    expect(lastCall().init.body).toBeUndefined();
    expect(headersOf(lastCall().init)["Content-Type"]).toBeUndefined();
  });

  it("passes the abort signal to fetch", async () => {
    fetchMock.mockResolvedValueOnce(json(200, { data: 1, error: null }));
    const controller = new AbortController();
    await apiRequest("/stats", { signal: controller.signal });
    expect(lastCall().init.signal).toBe(controller.signal);
  });

  describe("authorization header", () => {
    beforeEach(() => authSession.storeTokens("access-token", "refresh-token"));

    it("is sent to the API", async () => {
      fetchMock.mockResolvedValueOnce(json(200, { data: 1, error: null }));
      await apiRequest("/me");
      expect(headersOf(lastCall().init).Authorization).toBe("Bearer access-token");
    });

    it("is never sent to another origin", async () => {
      fetchMock.mockResolvedValueOnce(json(200, { data: 1, error: null }));
      await apiRequest("https://elsewhere.test/api/me");
      expect(headersOf(lastCall().init).Authorization).toBeUndefined();
    });

    it("is left out of anonymous requests", async () => {
      fetchMock.mockResolvedValueOnce(json(200, { data: 1, error: null }));
      await apiRequest("/login", { method: "POST", body: {}, authenticated: false });
      expect(headersOf(lastCall().init).Authorization).toBeUndefined();
    });

    it("is not invented when nobody is signed in", async () => {
      authSession.logout();
      fetchMock.mockResolvedValueOnce(json(200, { data: 1, error: null }));
      await apiRequest("/me");
      expect(headersOf(lastCall().init).Authorization).toBeUndefined();
    });
  });

  describe("errors", () => {
    it("carries the envelope's message and the status", async () => {
      fetchMock.mockResolvedValueOnce(json(409, { data: null, error: { error: "An extraction is already running." } }));

      const error = await apiRequest("/manual/drafts", { method: "POST" }).catch((failure: unknown) => failure);

      expect(error).toBeInstanceOf(ApiError);
      expect(error).toMatchObject({ name: "ApiError", status: 409, message: "An extraction is already running." });
    });

    it("falls back to a problem detail", async () => {
      fetchMock.mockResolvedValueOnce(json(400, { status: 400, detail: "from and to are required." }));
      await expect(apiRequest("/recap")).rejects.toMatchObject({ status: 400, message: "from and to are required." });
    });

    it.each([
      ["a non-JSON body", new Response("<html>Bad gateway</html>", { status: 502 })],
      ["an empty body", new Response(null, { status: 503 })],
      ["a blank message", json(500, { data: null, error: { error: "  " } })],
    ])("uses a generic message for %s", async (_, response) => {
      fetchMock.mockResolvedValueOnce(response);
      await expect(apiRequest("/stats")).rejects.toMatchObject({ message: "Request failed." });
    });

    it("throws a forbidden or failing request without retrying it", async () => {
      authSession.storeTokens("access-token", "refresh-token");
      fetchMock.mockResolvedValueOnce(json(403, { data: null, error: { error: "Forbidden." } }));

      await expect(apiRequest("/admin/platforms")).rejects.toMatchObject({ status: 403, message: "Forbidden." });
      expect(fetchMock).toHaveBeenCalledOnce();
    });
  });
});

describe("apiBlob", () => {
  it("downloads with the session's token", async () => {
    authSession.storeTokens("access-token", "refresh-token");
    fetchMock.mockResolvedValueOnce(
      new Response(new Uint8Array([0x89, 0x50, 0x4e]), { status: 200, headers: { "Content-Type": "image/png" } }),
    );

    const blob = await apiBlob("/admin/ai-generations/1/image");

    expect(blob.size).toBe(3);
    expect(blob.type).toBe("image/png");
    expect(lastCall().url).toBe("https://api.playdex.test/api/admin/ai-generations/1/image");
    expect(headersOf(lastCall().init).Authorization).toBe("Bearer access-token");
  });

  it("turns a failure into an ApiError", async () => {
    fetchMock.mockResolvedValueOnce(json(404, { data: null, error: { error: "Image not found." } }));
    await expect(apiBlob("/admin/ai-generations/1/image")).rejects.toMatchObject({ status: 404, message: "Image not found." });
  });
});

describe("error helpers", () => {
  it("recognises aborts only", () => {
    expect(isAbortError(new DOMException("The operation was aborted.", "AbortError"))).toBe(true);
    expect(isAbortError(new DOMException("Timed out.", "TimeoutError"))).toBe(false);
    expect(isAbortError(new Error("AbortError"))).toBe(false);
    expect(isAbortError("AbortError")).toBe(false);
  });

  it("gets a message from anything thrown", () => {
    expect(errorMessage(new ApiError(500, "Internal server error."))).toBe("Internal server error.");
    expect(errorMessage(new TypeError("Failed to fetch"))).toBe("Failed to fetch");
    expect(errorMessage("plain text")).toBe("plain text");
    expect(errorMessage(42)).toBe("42");
  });
});

describe("with an expired session", () => {
  beforeEach(() => {
    assign.mockReset();
    vi.stubGlobal("location", { ...window.location, assign });
    authSession.storeTokens("expired", "refresh-1");
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
});
