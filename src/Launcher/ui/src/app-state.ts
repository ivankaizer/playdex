import { createStore } from "./nav/store";

/** Game currently being launched / played (drives the launch overlay). */
export const launching = createStore<string | null>(null);

/** Game whose page is open, on top of the current tab. */
export const openGameId = createStore<string | null>(null);

/** The game whose tile opened the page, so Back returns focus to it once. */
let returnFocusGame: string | null = null;
export function takeReturnFocus(): string | null {
  const g = returnFocusGame;
  returnFocusGame = null;
  return g;
}

export const toast = createStore<string | null>(null);
let toastTimer = 0;
export function showToast(msg: string) {
  toast.set(msg);
  window.clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => toast.set(null), 2600);
}

export function launch(gameId: string) {
  launching.set(gameId);
}

export function openGame(gameId: string) {
  returnFocusGame = gameId;
  openGameId.set(gameId);
}

export function closeGame() {
  openGameId.set(null);
}
