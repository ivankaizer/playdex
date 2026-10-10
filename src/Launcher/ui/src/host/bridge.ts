/*
 * Bridge to the Windows host (src/Launcher/Host). The host is optional: in a normal browser `hostBridge` is null and
 * the UI falls back to keyboard, browser Gamepad input and a simulated launch.
 */

export type HostMessage =
  | { type: "dir"; dir: "up" | "down" | "left" | "right"; id: number }
  | { type: "action"; action: "confirm" | "back" | "x" | "y" | "lb" | "rb" | "view" | "menu" | "guide"; id: number }
  | { type: "home" }
  | { type: "session"; gameId: string; state: "starting" | "running" | "ended"; seconds: number }
  /** Launch request from outside the UI: playdex-launcher://launch/<gameId> or the host's --launch flag. */
  | { type: "launch"; gameId: string }
  /** Quick settings state, sent after every quick settings request. `error` is set when a change failed. */
  | ({ type: "quick-settings" } & QuickSettingsState)
  /**
   * The game running now (null: none), however it was started; sent when it changes. `since` is epoch ms, null when
   * unknown (Steam games started elsewhere).
   */
  | { type: "now-playing"; gameId: string | null; since?: number | null }
  /** A {type:"resume-game"} found no game window to switch to. */
  | { type: "resume-failed" }
  /** The desktop app's reply to a {type:"desktop"} request (see host/desktop.ts). */
  | { type: "desktop-result"; id: number; ok: boolean; result?: unknown; error?: unknown }
  /** The desktop app's state changed (backups, updates). */
  | { type: "desktop-state"; state: unknown };

export type AudioOutput = { id: string; name: string; isDefault: boolean };
export type DisplayState = {
  id: string;
  name: string;
  hdrSupported: boolean;
  hdrOn: boolean;
  /** Whole Hz (Windows reports 59.94 Hz as 59); 0 if unknown. */
  refreshRate: number;
  /** Rates at the current resolution, ascending. */
  refreshRates: number[];
};
/** The NVIDIA App's statistics overlay; `hotkey` as NVIDIA shows it ("Alt+R"). */
export type FpsCounter = { hotkey: string };
export type QuickSettingsState = {
  outputs: AudioOutput[];
  displays: DisplayState[];
  /** Absent when the NVIDIA overlay isn't running. */
  fpsCounter?: FpsCounter;
  error?: string;
};
export type PowerAction = "sleep" | "restart" | "shutdown";

export type UiMessage =
  | { type: "ack"; id: number; presented?: boolean }
  | { type: "ready" }
  | { type: "home-ready"; reason: string }
  /** The host launches only games from its own library, by id. */
  | { type: "launch"; gameId: string }
  | { type: "cancel"; gameId: string }
  /** Switch to the running game's window. */
  | { type: "resume-game" }
  /** Read the quick settings state; the host answers with a "quick-settings" message, as it does for the rest. */
  | { type: "quick-settings" }
  | { type: "audio-output"; id: string }
  | { type: "hdr"; display: string; on: boolean }
  | { type: "refresh-rate"; display: string; hz: number }
  /** Press the NVIDIA statistics overlay's hotkey (it toggles; the overlay's state can't be read). */
  | { type: "fps-counter" }
  | { type: "power"; action: PowerAction }
  /** A desktop app request (see host/desktop.ts). */
  | { type: "desktop"; id: number; method: string; params?: object };

interface WebView2 {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (e: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: { webview?: WebView2 };
  }
}

const webview = window.chrome?.webview ?? null;

/** True when running inside the Playdex host's WebView2. */
export const inHost = webview !== null;

const listeners = new Set<(m: HostMessage) => void>();

webview?.addEventListener("message", (e) => {
  const m = e.data as HostMessage;
  for (const fn of listeners) fn(m);
});

export function onHostMessage(fn: (m: HostMessage) => void) {
  listeners.add(fn);
  return () => {
    listeners.delete(fn);
  };
}

export function postToHost(m: UiMessage) {
  webview?.postMessage(m);
}

/** Tell the host the home screen is interactive, after the next frame has been produced. */
export function signalAfterFrame(m: UiMessage) {
  if (!webview) return;
  requestAnimationFrame(() => postToHost(m));
}
