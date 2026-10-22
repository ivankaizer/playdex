import { gameById, markPlayed, recentGames } from "./data/games";
import { inHost, onHostMessage, postToHost, signalAfterFrame, type SessionState } from "./host/bridge";
import { current, focusDefault, focusEl } from "./nav/focus";
import { createStore, useStore } from "./nav/store";

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
export const session = createStore<{ state: SessionState; seconds: number } | null>(null);

/**
 * Start a game. A game running now is resumed instead of started again. While another game runs the launch waits
 * in {@link launchGuard} to be confirmed: both then run, and both are recorded.
 */
export function launch(gameId: string) {
  if (isRunning(gameId)) {
    resumeGame(gameId);
    return;
  }
  const running = nowPlaying.get()?.gameId;
  if (running) {
    launchGuard.set({ gameId, runningId: running });
    return;
  }
  startLaunch(gameId);
}

function startLaunch(gameId: string) {
  launching.set(gameId);
  if (!inHost) return;
  session.set({ state: "starting", seconds: 0 });
  postToHost({ type: "launch", gameId });
}

/**
 * A launch asked for while another game runs, until it is confirmed or cancelled. A controller pressed in a game can
 * reach Playdex behind it, so A alone must not start a second game: the guard takes RB + Up (components/LaunchGuard).
 */
export const launchGuard = createStore<{ gameId: string; runningId: string } | null>(null);

export function confirmLaunch() {
  const guard = launchGuard.get();
  if (!guard) return;
  launchGuard.set(null);
  startLaunch(guard.gameId);
}

export function cancelLaunch() {
  launchGuard.set(null);
}

/** Leave the launch overlay. A game that has not started yet is cancelled; a running one keeps being tracked. */
export function leaveLaunch() {
  const gameId = launching.get();
  const state = session.get()?.state;
  if (gameId && inHost && (state === "starting" || state === "steam")) postToHost({ type: "cancel", gameId });
  launching.set(null);
  session.set(null);
}

onHostMessage((m) => {
  if (m.type === "launch") {
    if (!launching.get() && gameById(m.gameId)) launch(m.gameId);
    return;
  }
  if (m.type !== "session") return;
  // The home screen's order follows play: a game that starts or ends here is the last played.
  if (m.state === "running" || m.state === "ended") markPlayed(m.gameId);
  if (m.gameId !== launching.get()) return;
  if (m.state === "ended") {
    launching.set(null);
    session.set(null);
    // Back on the home screen: report once the next frame is produced (return-to-home budget).
    signalAfterFrame({ type: "home-ready", reason: "game-exit" });
    return;
  }
  session.set({ state: m.state, seconds: m.seconds });
});

/**
 * The game running now, however it was started (from here, Steam, a Playdex shortcut); `since` is epoch ms, null when
 * unknown. Only games in the library. A plain browser shows the first game with ?playing.
 */
export const nowPlaying = createStore<{ gameId: string; since: number | null } | null>(
  !inHost && new URLSearchParams(location.search).has("playing") && recentGames[0]
    ? { gameId: recentGames[0].game.id, since: Date.now() - 754_000 }
    : null,
);

/** Every game running now (the one {@link nowPlaying} shows among them): starting one of them resumes it. */
export const runningGames = createStore<string[]>([]);

export function isRunning(gameId: string) {
  return nowPlaying.get()?.gameId === gameId || runningGames.get().includes(gameId);
}

/** {@link isRunning}, re-rendering when it changes. */
export function useIsRunning(gameId: string) {
  const playing = useStore(nowPlaying)?.gameId === gameId;
  const running = useStore(runningGames).includes(gameId);
  return playing || running;
}

onHostMessage((m) => {
  if (m.type === "now-playing") {
    nowPlaying.set(m.gameId && gameById(m.gameId) ? { gameId: m.gameId, since: m.since ?? null } : null);
    runningGames.set(m.games ?? (m.gameId ? [m.gameId] : []));
    // A game started elsewhere (Steam, a shortcut) is the last played too.
    if (m.gameId) markPlayed(m.gameId);
  } else if (m.type === "resume-failed") {
    showToast("Couldn't find the game's window");
  } else if (m.type === "close-failed") {
    showToast(m.message);
  } else if (m.type === "open-steam-failed") {
    showToast("Steam isn't installed on this PC");
  } else if (m.type === "open-app-failed") {
    showToast(m.message);
  }
});

/** Open Steam: its Big Picture in Big Picture mode, its window in desktop mode. */
export function openSteam() {
  if (!inHost) {
    showToast("Opening Steam works in the Playdex app on Windows");
    return;
  }
  postToHost({ type: "open-steam" });
}

/** Open an app (Discord, a browser), or switch to it when it is running. */
export function openApp(id: string, name: string) {
  if (!inHost) {
    showToast(`Opening ${name} works in the Playdex app on Windows`);
    return;
  }
  postToHost({ type: "open-app", id });
}

/** Switch to a running game's window: `gameId`'s, else the one now playing. */
export function resumeGame(gameId?: string) {
  if (!inHost) {
    showToast("Resume works in the Playdex app on Windows");
    return;
  }
  postToHost({ type: "resume-game", gameId: gameId ?? nowPlaying.get()?.gameId });
}

/** The running game armed for force close: a second request within {@link CLOSE_ARM_MS} closes it. */
export const closeArmed = createStore<string | null>(null);
const CLOSE_ARM_MS = 3000;
let closeDisarm = 0;

/**
 * Force-close the running game, as Task Manager's End task would (no chance to save). The first request arms it and
 * asks to confirm; a second within 3 s closes it. Its exit then shows as usual (the top bar and Home stop saying so).
 */
export function forceCloseGame(gameId: string) {
  const title = gameById(gameId)?.title ?? "the game";
  window.clearTimeout(closeDisarm);
  if (closeArmed.get() !== gameId) {
    closeArmed.set(gameId);
    closeDisarm = window.setTimeout(() => closeArmed.set(null), CLOSE_ARM_MS);
    showToast(`Press again to close ${title}. Unsaved progress is lost.`);
    return;
  }
  closeArmed.set(null);
  showToast(`Closing ${title}…`);
  if (!inHost) {
    // The browser stand-in: the game exits at once.
    if (nowPlaying.get()?.gameId === gameId) nowPlaying.set(null);
    return;
  }
  postToHost({ type: "close-game", gameId });
}

export function openGame(gameId: string) {
  returnFocusGame = gameId;
  openGameId.set(gameId);
}

export function closeGame() {
  openGameId.set(null);
}

/** The game whose artwork picker is open, over its page. */
export const artPickerGame = createStore<string | null>(null);
let artPickerOpener: HTMLElement | null = null;

export function openArtPicker(gameId: string) {
  if (launching.get() || artPickerGame.get()) return;
  artPickerOpener = current();
  artPickerGame.set(gameId);
}

/** Close the picker and give focus back to the button that opened it. */
export function closeArtPicker() {
  if (!artPickerGame.get()) return;
  artPickerGame.set(null);
  const opener = artPickerOpener;
  artPickerOpener = null;
  window.setTimeout(() => (opener?.isConnected ? focusEl(opener) : focusDefault()));
}

/** The quick settings panel (sound output, HDR, power), over whatever screen is open. */
export const quickSettingsOpen = createStore(false);
let quickSettingsOpener: HTMLElement | null = null;

export function openQuickSettings() {
  if (launching.get() || quickSettingsOpen.get()) return;
  quickSettingsOpener = current();
  quickSettingsOpen.set(true);
}

/** Close the panel and give focus back to whatever opened it. */
export function closeQuickSettings() {
  if (!quickSettingsOpen.get()) return;
  quickSettingsOpen.set(false);
  const opener = quickSettingsOpener;
  quickSettingsOpener = null;
  // After the screen behind is no longer inert.
  window.setTimeout(() => (opener?.isConnected ? focusEl(opener) : focusDefault()));
}
