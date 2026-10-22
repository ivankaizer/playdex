import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError, apiBaseUrl, apiBlob, apiRequest, buildUrl, errorMessage, isAbortError } from "./api";
import { authSession } from "./auth";
import { getConfig, setConfig } from "./config";

// Token refresh (what happens after a 401) is covered with the auth flow; these tests stay away from 401s.

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
