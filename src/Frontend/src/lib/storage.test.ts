import { afterEach, describe, expect, it } from "vitest";
import { storage } from "./storage";

const original = Object.getOwnPropertyDescriptor(window, "localStorage");

/** Replaces window.localStorage for one test. */
function useLocalStorage(descriptor: PropertyDescriptor): void {
  Object.defineProperty(window, "localStorage", { configurable: true, ...descriptor });
}

afterEach(() => {
  if (original) Object.defineProperty(window, "localStorage", original);
  else delete (window as { localStorage?: Storage }).localStorage;
  storage.remove("playdex.test");
  window.localStorage.removeItem("playdex.test");
});

describe("storage", () => {
  it("reads and writes localStorage", () => {
    storage.set("playdex.test", "one");
    expect(window.localStorage.getItem("playdex.test")).toBe("one");
    expect(storage.get("playdex.test")).toBe("one");
    storage.remove("playdex.test");
    expect(storage.get("playdex.test")).toBeNull();
  });

  it("keeps working in memory when the browser blocks storage", () => {
    useLocalStorage({
      get() {
        throw new DOMException("The operation is insecure.", "SecurityError");
      },
    });

    expect(storage.get("playdex.test")).toBeNull();
    storage.set("playdex.test", "kept");
    expect(storage.get("playdex.test")).toBe("kept");
    storage.remove("playdex.test");
    expect(storage.get("playdex.test")).toBeNull();
  });

  it("prefers a value that could not be written over the stale one in storage", () => {
    window.localStorage.setItem("playdex.test", "stale");
    const full = {
      getItem: (key: string) => (key === "playdex.test" ? "stale" : null),
      setItem: () => {
        throw new DOMException("Quota exceeded.", "QuotaExceededError");
      },
      removeItem: () => undefined,
    };
    useLocalStorage({ value: full, writable: true });

    storage.set("playdex.test", "fresh");

    expect(storage.get("playdex.test")).toBe("fresh");
  });
});
