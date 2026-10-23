import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

type StorageModule = typeof import("./storage");

const realStorage = Object.getOwnPropertyDescriptor(globalThis, "localStorage")!;

/** A fresh copy of the module, so the in-memory fallback starts empty for every test. */
async function load(): Promise<StorageModule["storage"]> {
  vi.resetModules();
  return (await import("./storage")).storage;
}

function failing(method: "getItem" | "setItem" | "removeItem") {
  // The test setup may swap jsdom's Storage for an in-memory one, so patch whichever class backs localStorage.
  const backing = Object.getPrototypeOf(localStorage) as Storage;
  return vi.spyOn(backing, method).mockImplementation(() => {
    throw new DOMException("The quota has been exceeded.", "QuotaExceededError");
  });
}

function blockLocalStorage() {
  Object.defineProperty(globalThis, "localStorage", {
    configurable: true,
    get() {
      throw new DOMException("Access is denied for this document.", "SecurityError");
    },
  });
}

beforeEach(() => {
  localStorage.clear();
});

afterEach(() => {
  vi.restoreAllMocks();
  Object.defineProperty(globalThis, "localStorage", realStorage);
});

describe("storage", () => {
  it("reads, writes and removes through localStorage", async () => {
    const storage = await load();

    storage.set("playdex.theme", "dark");
    expect(localStorage.getItem("playdex.theme")).toBe("dark");
    expect(storage.get("playdex.theme")).toBe("dark");

    storage.remove("playdex.theme");
    expect(localStorage.getItem("playdex.theme")).toBeNull();
    expect(storage.get("playdex.theme")).toBeNull();
  });

  it("returns null for keys that were never written", async () => {
    const storage = await load();
    expect(storage.get("playdex.missing")).toBeNull();
  });

  it("keeps working for the page when localStorage is blocked outright", async () => {
    const storage = await load();
    blockLocalStorage();

    expect(storage.get("playdex.token")).toBeNull();
    expect(() => storage.set("playdex.token", "access")).not.toThrow();
    expect(storage.get("playdex.token")).toBe("access");
    expect(() => storage.remove("playdex.token")).not.toThrow();
    expect(storage.get("playdex.token")).toBeNull();
  });

  it("keeps a value in memory when writing fails, instead of the stale stored one", async () => {
    const storage = await load();
    localStorage.setItem("playdex.theme", "light");
    failing("setItem");

    storage.set("playdex.theme", "dark");

    expect(storage.get("playdex.theme")).toBe("dark");
  });

  it("goes back to localStorage once a write succeeds again", async () => {
    const storage = await load();
    const setItem = failing("setItem");
    storage.set("playdex.theme", "dark");
    setItem.mockRestore();

    storage.set("playdex.theme", "light");

    expect(localStorage.getItem("playdex.theme")).toBe("light");
    expect(storage.get("playdex.theme")).toBe("light");
  });

  it("treats an unreadable value as missing", async () => {
    const storage = await load();
    localStorage.setItem("playdex.email", "a@b.test");
    failing("getItem");

    expect(storage.get("playdex.email")).toBeNull();
  });

  it("removes the in-memory value even when localStorage refuses the removal", async () => {
    const storage = await load();
    failing("setItem");
    failing("removeItem");
    storage.set("playdex.refresh", "refresh");

    expect(() => storage.remove("playdex.refresh")).not.toThrow();
    expect(storage.get("playdex.refresh")).toBeNull();
  });

  it("lets a sign-in last for the page when storage is blocked", async () => {
    vi.resetModules();
    const { authSession } = await import("./auth");
    blockLocalStorage();

    authSession.storeLogin({ userId: 1, email: "a@b.test", isAdmin: true, accessToken: "access", refreshToken: "refresh" });
    expect(authSession.isLoggedIn()).toBe(true);
    expect(authSession.getEmail()).toBe("a@b.test");
    expect(authSession.isAdmin()).toBe(true);

    authSession.logout();
    expect(authSession.isLoggedIn()).toBe(false);
    expect(authSession.getRefreshToken()).toBeNull();
  });
});
