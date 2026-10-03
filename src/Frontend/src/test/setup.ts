import "@testing-library/jest-dom/vitest";

// Newer Node versions expose an experimental global `localStorage` that shadows jsdom's
// implementation (and is undefined without --localstorage-file). Provide an in-memory Storage.
class MemoryStorage implements Storage {
  private items = new Map<string, string>();
  get length() {
    return this.items.size;
  }
  clear() {
    this.items.clear();
  }
  getItem(key: string) {
    return this.items.has(key) ? this.items.get(key)! : null;
  }
  key(index: number) {
    return [...this.items.keys()][index] ?? null;
  }
  removeItem(key: string) {
    this.items.delete(key);
  }
  setItem(key: string, value: string) {
    this.items.set(key, String(value));
  }
}

for (const name of ["localStorage", "sessionStorage"] as const) {
  let usable = false;
  try {
    usable = typeof globalThis[name]?.getItem === "function";
  } catch {
    usable = false;
  }
  if (!usable) {
    Object.defineProperty(globalThis, name, { value: new MemoryStorage(), configurable: true, writable: true });
  }
}
