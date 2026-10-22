// Browsers can refuse localStorage (blocked site data, some private windows, a full quota); then even reading
// `localStorage` throws. Values that cannot be stored are kept in memory instead, so the app still works for
// the life of the page rather than failing to render.
const fallback = new Map<string, string>();

export const storage = {
  get(key: string): string | null {
    if (fallback.has(key)) return fallback.get(key)!;
    try {
      return localStorage.getItem(key);
    } catch {
      return null;
    }
  },
  set(key: string, value: string): void {
    try {
      localStorage.setItem(key, value);
      fallback.delete(key);
    } catch {
      fallback.set(key, value);
    }
  },
  remove(key: string): void {
    fallback.delete(key);
    try {
      localStorage.removeItem(key);
    } catch {
      // Nothing was stored there.
    }
  },
};
