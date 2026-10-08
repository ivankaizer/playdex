import { useSyncExternalStore } from "react";

/** Minimal external store: components subscribe to one value and re-render only when it changes. */
export function createStore<T>(initial: T) {
  let value = initial;
  const listeners = new Set<() => void>();
  return {
    get: () => value,
    set(next: T) {
      if (Object.is(next, value)) return;
      value = next;
      listeners.forEach((l) => l());
    },
    subscribe(l: () => void) {
      listeners.add(l);
      return () => listeners.delete(l);
    },
  };
}

export type Store<T> = ReturnType<typeof createStore<T>>;

export function useStore<T>(store: Store<T>): T {
  return useSyncExternalStore(store.subscribe, store.get, store.get);
}

/** The game under focus. Only backdrops and detail panels subscribe; lists never re-render on focus moves. */
export const focusedGame = createStore<string | null>(null);

/** Which kind of focusable is under focus; drives the button-hint bar. */
export const focusedKind = createStore<string>("");

export type InputMode = "pad" | "pointer";
export const inputMode = createStore<InputMode>("pad");
