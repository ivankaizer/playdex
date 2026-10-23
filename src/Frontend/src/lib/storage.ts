/**
 * localStorage that never throws. Blocked storage (privacy settings, some embedded browsers) throws on any access, and
 * a full one throws on write; values then live in memory for the rest of the page's life, so the app keeps working
 * and only stops remembering across reloads.
 */
const memory = new Map<string, string>();

function local(): Storage | null {
  try {
    return window.localStorage ?? null;
  } catch {
    return null;
  }
}

export const storage = {
  get(key: string): string | null {
    // A value that could not be written to localStorage is newer than whatever is there.
    if (memory.has(key)) return memory.get(key)!;
    try {
      return local()?.getItem(key) ?? null;
    } catch {
      return null;
    }
  },
  set(key: string, value: string): void {
    try {
      const store = local();
      if (store) {
        store.setItem(key, value);
        memory.delete(key);
        return;
      }
    } catch {
      // Blocked or full: kept in memory below.
    }
    memory.set(key, value);
  },
  remove(key: string): void {
    memory.delete(key);
    try {
      local()?.removeItem(key);
    } catch {
      // Nothing stored there to remove.
    }
  },
};
