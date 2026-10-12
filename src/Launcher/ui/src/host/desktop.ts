/*
 * The desktop app's side of the bridge (src/Desktop). In the desktop app the page manages the games added to Playdex
 * and the app's settings, what the WinForms Wrapper's tray window did. Requests go out as {type:"desktop", id, method,
 * params} and come back as {type:"desktop-result", id, ok, result | error}; the host also pushes {type:"desktop-state"}
 * when backups or updates change. In a plain browser a stand-in state answers, so the screens can be worked on in Vite.
 */
import { createStore } from "../nav/store";
import { inHost, onHostMessage, postToHost } from "./bridge";

declare global {
  interface Window {
    /** "desktop" in desktop mode, "fse" in Big Picture; absent in a plain browser. */
    __PLAYDEX_MODE__?: "desktop" | "fse";
  }
}

/** True in desktop mode (or with ?mode=desktop in a browser); false in Big Picture. */
export const isDesktop =
  window.__PLAYDEX_MODE__ === "desktop" || new URLSearchParams(location.search).get("mode") === "desktop";

export type BackupKind = "off" | "due" | "running" | "success" | "failed" | "unsupported";

export interface DesktopGame {
  gameId: string;
  displayName: string;
  apiGameId: string;
  gameProvider: string;
  /** Null: the install's default platform. */
  platform?: string | null;
  launchOptions: string;
  executablePath: string;
  executableExists: boolean;
  dailyBackup: boolean;
  backup: { kind: BackupKind; text: string };
}

export interface DesktopSettings {
  baseUrl: string;
  /** Keys never reach the page; only whether one is saved. */
  apiKeySet: boolean;
  platform: string;
  steamGridDbKeySet: boolean;
  ludusaviPath: string;
  startWithWindows: boolean;
}

export interface UpdateView {
  kind: "unavailable" | "idle" | "checking" | "downloading" | "ready" | "applying" | "failed";
  message: string;
  version?: string;
}

export interface FseView {
  state: "unavailable" | "available" | "installing" | "installed" | "failed";
  message: string;
}

/** How Playdex opens: a window and the tray, or full screen and controller first. */
export type AppMode = "desktop" | "bigPicture";

export interface DesktopState {
  version: string;
  mode: AppMode;
  games: DesktopGame[];
  settings: DesktopSettings;
  update: UpdateView;
  fse: FseView;
  launcherDirectory: string;
  runAvailable: boolean;
}

export interface PlatformOption {
  key: string;
  name: string;
}

export interface MessageView {
  message: string;
  warning?: boolean;
}

export const desktopState = createStore<DesktopState | null>(null);

const pending = new Map<number, { resolve: (value: unknown) => void; reject: (error: Error) => void }>();
let nextId = 1;

onHostMessage((m) => {
  if (m.type === "desktop-state") {
    desktopState.set(m.state as DesktopState);
    return;
  }
  if (m.type !== "desktop-result") return;
  const request = pending.get(m.id);
  if (!request) return;
  pending.delete(m.id);
  if (m.ok) request.resolve(m.result);
  else request.reject(new Error(typeof m.error === "string" ? m.error : "The request failed."));
});

const isState = (value: unknown): value is DesktopState =>
  typeof value === "object" && value !== null && "games" in value && "settings" in value;

/** Ask the desktop app. Replies that carry the whole state also update {@link desktopState}. */
export async function rpc<T>(method: string, params?: object): Promise<T> {
  const result = inHost
    ? await new Promise<unknown>((resolve, reject) => {
        const id = nextId++;
        pending.set(id, { resolve, reject });
        postToHost({ type: "desktop", id, method, params });
      })
    : await demo(method, params as Record<string, unknown> | undefined);
  if (isState(result)) desktopState.set(result);
  return result as T;
}

export function loadDesktopState() {
  return rpc<DesktopState>("state");
}

/** Save the mode and switch to it: the app replaces this window with one in the other mode. */
export function switchMode(mode: AppMode) {
  return rpc<DesktopState>("mode.set", { mode });
}

/* ---------- Stand-in for a plain browser ---------- */

let demoState: DesktopState = {
  version: "2.0.0",
  mode: isDesktop ? "desktop" : "bigPicture",
  launcherDirectory: "C:\\Users\\you\\AppData\\Local\\Playdex\\launchers",
  runAvailable: true,
  games: [
    {
      gameId: "SplitFiction",
      displayName: "Split Fiction",
      apiGameId: "2001120",
      gameProvider: "steam",
      platform: null,
      launchOptions: "",
      executablePath: "D:\\Games\\Split Fiction\\SplitFiction.exe",
      executableExists: true,
      dailyBackup: true,
      backup: { kind: "success", text: "Backed up 10/10/2026 09:12." },
    },
    {
      gameId: "Cronos",
      displayName: "Cronos: The New Dawn",
      apiGameId: "1850570",
      gameProvider: "steam",
      platform: "gog",
      launchOptions: "-dx12",
      executablePath: "E:\\GOG\\Cronos\\Cronos.exe",
      executableExists: true,
      dailyBackup: false,
      backup: { kind: "off", text: "No backups yet." },
    },
  ],
  settings: {
    baseUrl: "https://dash.playdex.dev/api",
    apiKeySet: true,
    platform: "pc",
    steamGridDbKeySet: false,
    ludusaviPath: "",
    startWithWindows: true,
  },
  update: { kind: "idle", message: "Playdex is up to date." },
  fse: { state: "available", message: "Use Playdex in Big Picture as the Full Screen Experience home." },
};

async function demo(method: string, params?: Record<string, unknown>): Promise<unknown> {
  await new Promise((r) => setTimeout(r, 250));
  const game = demoState.games.find((g) => g.gameId === params?.gameId);
  switch (method) {
    case "state":
      return demoState;
    case "platforms":
      return {
        platforms: [
          { key: "pc", name: "PC" },
          { key: "steam-deck", name: "Steam Deck" },
          { key: "gog", name: "GOG" },
        ],
      };
    case "game.update":
      if (game) {
        Object.assign(game, {
          displayName: params!.displayName,
          apiGameId: params!.apiGameId,
          launchOptions: params!.launchOptions ?? "",
          platform: params!.platform || null,
          dailyBackup: !!params!.dailyBackup,
        });
      }
      return (demoState = { ...demoState, games: [...demoState.games] });
    case "game.remove":
      return (demoState = { ...demoState, games: demoState.games.filter((g) => g !== game) });
    case "game.add":
      return demoState;
    case "settings.save":
      demoState = {
        ...demoState,
        settings: {
          ...demoState.settings,
          baseUrl: String(params!.baseUrl ?? ""),
          platform: String(params!.platform ?? "pc"),
          ludusaviPath: String(params!.ludusaviPath ?? ""),
          startWithWindows: !!params!.startWithWindows,
          apiKeySet: demoState.settings.apiKeySet || !!params!.apiKey,
          steamGridDbKeySet: demoState.settings.steamGridDbKeySet || !!params!.steamGridDbKey,
        },
      };
      return demoState;
    case "mode.set":
      // The browser stand-in for replacing the window: reload in the other layout.
      location.search = params?.mode === "desktop" ? "?mode=desktop" : "";
      return demoState;
    case "settings.browseLudusavi":
      return { path: "C:\\Tools\\ludusavi.exe" };
    default:
      return { message: `${method} works in the Playdex app on Windows.` };
  }
}
