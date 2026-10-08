import { gameById } from "./data/games";
import { inHost, onHostMessage, postToHost, signalAfterFrame } from "./host/bridge";
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

/** Session state reported by the Windows host for the launching game. Null in a plain browser (simulated). */
export const session = createStore<{ state: "starting" | "running" | "ended"; seconds: number } | null>(null);

export function launch(gameId: string) {
  launching.set(gameId);
  if (!inHost) return;
  const game = gameById(gameId);
  session.set({ state: "starting", seconds: 0 });
  postToHost({
    type: "launch",
    gameId,
    title: game.title,
    steamAppId: game.source === "steam" ? game.art : undefined,
  });
}

/** Leave the launch overlay. A game that has not started yet is cancelled; a running one keeps being tracked. */
export function leaveLaunch() {
  const gameId = launching.get();
  if (gameId && inHost && session.get()?.state === "starting") postToHost({ type: "cancel", gameId });
  launching.set(null);
  session.set(null);
}

onHostMessage((m) => {
  if (m.type === "launch") {
    if (!launching.get() && gameById(m.gameId)) launch(m.gameId);
    return;
  }
  if (m.type !== "session" || m.gameId !== launching.get()) return;
  if (m.state === "ended") {
    launching.set(null);
    session.set(null);
    // Back on the home screen: report once the next frame is produced (return-to-home budget).
    signalAfterFrame({ type: "home-ready", reason: "game-exit" });
    return;
  }
  session.set({ state: m.state, seconds: m.seconds });
});

export function openGame(gameId: string) {
  returnFocusGame = gameId;
  openGameId.set(gameId);
}

export function closeGame() {
  openGameId.set(null);
}
