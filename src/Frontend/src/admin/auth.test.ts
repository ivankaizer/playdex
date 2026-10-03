import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { authSession } from "@/lib/auth";
import { adminLogin, checkAdminSession, NotAdministratorMessage } from "./auth";

function json(status: number, body: unknown): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  localStorage.clear();
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

const login = (isAdmin: boolean) => ({
  data: { userId: 1, email: "a@b.test", isAdmin, accessToken: "access", refreshToken: "refresh" },
  error: null,
});

describe("adminLogin", () => {
  it("stores the session for administrators", async () => {
    fetchMock.mockResolvedValueOnce(json(200, login(true)));
    await expect(adminLogin(" a@b.test ", "pw")).resolves.toEqual({ ok: true, email: "a@b.test" });
    expect(authSession.getAccessToken()).toBe("access");
    const [url, init] = fetchMock.mock.calls[0]!;
    expect(String(url)).toMatch(/\/api\/login$/);
    expect(JSON.parse(String(init?.body))).toEqual({ email: "a@b.test", password: "pw" });
  });

  it("rejects non-administrators and leaves the browser signed out", async () => {
    authSession.storeTokens("old-access", "old-refresh");
    fetchMock.mockResolvedValueOnce(json(200, login(false)));
    await expect(adminLogin("a@b.test", "pw")).resolves.toEqual({ ok: false, error: NotAdministratorMessage });
    expect(authSession.getAccessToken()).toBeNull();
    expect(authSession.getRefreshToken()).toBeNull();
  });

  it("reports invalid credentials without storing anything", async () => {
    fetchMock.mockResolvedValueOnce(json(401, { data: null, error: { error: "Invalid credentials." } }));
    const result = await adminLogin("a@b.test", "wrong");
    expect(result).toEqual({ ok: false, error: "Invalid email or password." });
    expect(authSession.isLoggedIn()).toBe(false);
  });

  it("requires both fields before calling the API", async () => {
    await expect(adminLogin("", "pw")).resolves.toMatchObject({ ok: false });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("checkAdminSession", () => {
  it("is anonymous without a token", async () => {
    await expect(checkAdminSession()).resolves.toEqual({ status: "anonymous" });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("re-checks the role through /api/me", async () => {
    authSession.storeTokens("access", "refresh");
    fetchMock.mockResolvedValueOnce(json(200, { data: { userId: 1, email: "a@b.test", isAdmin: true }, error: null }));
    await expect(checkAdminSession()).resolves.toEqual({ status: "admin", email: "a@b.test" });
    fetchMock.mockResolvedValueOnce(json(200, { data: { userId: 1, email: "a@b.test", isAdmin: false }, error: null }));
    await expect(checkAdminSession()).resolves.toEqual({ status: "forbidden" });
    // A demoted admin keeps their player session; only admin pages are refused.
    expect(authSession.isLoggedIn()).toBe(true);
  });

  it("clears an expired session that cannot be refreshed", async () => {
    authSession.storeTokens("access", "refresh");
    fetchMock.mockImplementation(async () => json(401, { data: null, error: { error: "Invalid token." } }));
    await expect(checkAdminSession()).resolves.toEqual({ status: "anonymous" });
    expect(authSession.isLoggedIn()).toBe(false);
  });
});
