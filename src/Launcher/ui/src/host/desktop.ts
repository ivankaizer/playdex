/*
 * The desktop app's side of the bridge (src/Desktop). In the desktop app the page manages the games added to Playdex
 * and the app's settings, what the WinForms Wrapper's tray window did. Requests go out as {type:"desktop", id, method,
 * params} and come back as {type:"desktop-result", id, ok, result | error}; the host also pushes {type:"desktop-state"}
 * when backups or updates change. In a plain browser a stand-in state answers, so the screens can be worked on in Vite.
 */
import { gameById, type GameImages } from "../data/games";
import { createStore } from "../nav/store";
import { inHost, onHostMessage, postToHost } from "./bridge";

declare global {
  interface Window {
    /** "desktop" in desktop mode, "fse" in Big Picture; absent in a plain browser. */
    __PLAYDEX_MODE__?: "desktop" | "fse";
  }
}

/** True in desktop mode (or with ?mode=desktop in a browser); false in Big Picture. */
export const isDesktop = window.__PLAYDEX_MODE__ === "desktop" || new URLSearchParams(location.search).get("mode") === "desktop";

export type BackupKind = "off" | "due" | "running" | "success" | "failed" | "unsupported";

export interface BackupView {
  kind: BackupKind;
  /** Manage's wording, with the date and any error. */
  text: string;
  /** The last backup that went through, epoch ms. */
  lastSuccessAt?: number;
}

export interface DesktopGame {
  gameId: string;
  /** The game's id in the library ("playdex-<gameId>"). */
  libraryId: string;
  displayName: string;
  apiGameId: string;
  gameProvider: string;
  /** Null: the install's default platform. */
  platform?: string | null;
  launchOptions: string;
  executablePath: string;
  executableExists: boolean;
  dailyBackup: boolean;
  backup: BackupView;
}

/** An app Big Picture opens from quick settings. */
export interface DesktopApp {
  id: string;
  name: string;
  kind: string;
  /** What starts it on this PC; empty when it isn't here. */
  path: string;
  launchOptions: string;
  /** On this PC, so Big Picture lists it. */
  available: boolean;
}

/** A game installed through the Xbox app that isn't added to Playdex yet. */
export interface DesktopXboxGame {
  /** Its app user model id, what game.addXbox takes. */
  id: string;
  title: string;
  executablePath: string;
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
  apps: DesktopApp[];
  /** Apps found on this PC that aren't in the list (Discord, the default browser). */
  suggestedApps: DesktopApp[];
  /** Xbox app games on this PC that aren't added yet. */
  xboxGames: DesktopXboxGame[];
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

/** The artwork a game's page can change: poster, wide cover, background and logo. */
export type ArtKind = "portrait" | "capsule" | "hero" | "logo";

/** The kinds the user picked for a game, each with the SteamGridDB image it is. */
export type PickedArt = Partial<Record<ArtKind, number>>;

/** A SteamGridDB game to take artwork from. */
export interface ArtSource {
  id: number;
  name: string;
  year?: number;
}

export interface ArtImage {
  id: number;
  url: string;
  thumb: string;
  width: number;
  height: number;
  style?: string;
}

/** "art.search": SteamGridDB's games for a library game, best match first. */
export interface ArtSearchView {
  games: ArtSource[];
  picked: PickedArt;
}

/** "art.list": a page of a SteamGridDB game's images of one kind. */
export interface ArtPage {
  images: ArtImage[];
  more: boolean;
}

/** "art.set" and "art.reset": the library game's artwork afterwards (the next art in line where none is picked). */
export interface ArtView {
  images?: GameImages;
  picked: PickedArt;
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

/** Start Playdex in the tray when Windows starts. Applies at once, apart from the settings form. */
export function setStartWithWindows(enabled: boolean) {
  return rpc<DesktopState>("settings.startWithWindows", { enabled });
}

/* ---------- Stand-in for a plain browser ---------- */

let demoState: DesktopState = {
  version: "2.0.0",
  mode: isDesktop ? "desktop" : "bigPicture",
  launcherDirectory: "C:\\Users\\you\\AppData\\Local\\Playdex\\launchers",
  runAvailable: true,
  // The demo library's first games stand in for games added to Playdex, so their pages and tiles show save status.
  games: [
    {
      gameId: "Hades2",
      libraryId: "hades2",
      displayName: "Hades II",
      apiGameId: "1145350",
      gameProvider: "steam",
      platform: null,
      launchOptions: "",
      executablePath: "D:\\Games\\Hades II\\Hades2.exe",
      executableExists: true,
      dailyBackup: true,
      backup: { kind: "success", text: "Backed up today 09:12.", lastSuccessAt: new Date().setHours(9, 12, 0, 0) },
    },
    {
      gameId: "Balatro",
      libraryId: "balatro",
      displayName: "Balatro",
      apiGameId: "2379780",
      gameProvider: "steam",
      platform: "gog",
      launchOptions: "-dx12",
      executablePath: "E:\\GOG\\Balatro\\Balatro.exe",
      executableExists: true,
      dailyBackup: false,
      backup: { kind: "off", text: "No backups yet." },
    },
  ],
  apps: [
    {
      id: "discord",
      name: "Discord",
      kind: "discord",
      path: "C:\\Users\\you\\AppData\\Local\\Discord\\Update.exe",
      launchOptions: "",
      available: true,
    },
  ],
  suggestedApps: [
    {
      id: "browser",
      name: "Microsoft Edge",
      kind: "browser",
      path: "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
      launchOptions: "",
      available: true,
    },
  ],
  xboxGames: [
    {
      id: "Contoso.StarlightRally_8wekyb3d8bbwe!Game",
      title: "Starlight Rally",
      executablePath: "C:\\XboxGames\\Starlight Rally\\Content\\StarlightRally.exe",
    },
  ],
  settings: {
    baseUrl: "https://dash.playdex.dev/api",
    // ?nokey shows Playdex without an API key.
    apiKeySet: !new URLSearchParams(location.search).has("nokey"),
    platform: "pc",
    steamGridDbKeySet: false,
    ludusaviPath: "C:\\Tools\\ludusavi.exe",
    startWithWindows: true,
  },
  // ?update shows an update that is ready to install.
  update: new URLSearchParams(location.search).has("update")
    ? { kind: "ready", message: "Playdex 2.1.0 is ready. Restart to update.", version: "2.1.0" }
    : { kind: "idle", message: "Playdex is up to date." },
  fse: { state: "available", message: "Use Playdex in Big Picture as the Full Screen Experience home." },
};

const demoPicked = new Map<string, PickedArt>();

/** The browser stand-in for SteamGridDB: the demo game's own Steam CDN art, a few of each kind. */
function demoArt(libraryId: string, kind: ArtKind): ArtPage {
  const appId = gameById(libraryId)?.art;
  const files: Record<ArtKind, string[]> = {
    portrait: ["library_600x900.jpg", "library_600x900_2x.jpg"],
    capsule: ["header.jpg", "capsule_616x353.jpg", "capsule_467x181.jpg"],
    hero: ["library_hero.jpg", "page_bg_generated_v6b.jpg"],
    logo: ["logo.png"],
  };
  if (!appId) return { images: [], more: false };
  const base = (Object.keys(files).indexOf(kind) + 1) * 100;
  return {
    images: files[kind].map((file, i) => {
      const url = `https://cdn.cloudflare.steamstatic.com/steam/apps/${appId}/${file}`;
      return { id: base + i, url, thumb: url, width: 0, height: 0, style: "official" };
    }),
    more: false,
  };
}

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
    case "game.dailyBackup":
      if (game) {
        game.dailyBackup = !!params!.enabled;
        if (game.dailyBackup && game.backup.kind === "off") game.backup = { kind: "due", text: "Daily backup is due." };
      }
      return (demoState = { ...demoState, games: [...demoState.games] });
    case "game.backup":
      if (game) game.backup = { kind: "success", text: "Backed up just now.", lastSuccessAt: Date.now() };
      desktopState.set((demoState = { ...demoState, games: [...demoState.games] }));
      return { message: `${game?.displayName}'s saves were backed up.` };
    case "game.remove":
      return (demoState = { ...demoState, games: demoState.games.filter((g) => g !== game) });
    case "game.add":
    case "app.add":
      return demoState;
    case "game.addXbox": {
      const found = demoState.xboxGames.find((g) => g.id === params?.id);
      if (!found) return demoState;
      const gameId = found.executablePath.replace(/^.*\\|\.exe$/gi, "");
      return (demoState = {
        ...demoState,
        games: [
          ...demoState.games,
          {
            gameId,
            libraryId: `playdex-${gameId}`,
            displayName: found.title,
            apiGameId: "1234567890",
            gameProvider: "xbox",
            platform: null,
            launchOptions: "",
            executablePath: found.executablePath,
            executableExists: true,
            dailyBackup: false,
            backup: { kind: "off", text: "No backups yet." },
          },
        ],
        xboxGames: demoState.xboxGames.filter((g) => g !== found),
      });
    }
    case "app.addFound": {
      const found = demoState.suggestedApps.find((a) => a.kind === params?.kind);
      if (!found) return demoState;
      return (demoState = {
        ...demoState,
        apps: [...demoState.apps, found],
        suggestedApps: demoState.suggestedApps.filter((a) => a !== found),
      });
    }
    case "app.update":
      return (demoState = {
        ...demoState,
        apps: demoState.apps.map((a) =>
          a.id === params?.id ? { ...a, name: String(params!.name), launchOptions: String(params!.launchOptions ?? "") } : a,
        ),
      });
    case "app.remove": {
      const removed = demoState.apps.find((a) => a.id === params?.id);
      return (demoState = {
        ...demoState,
        apps: demoState.apps.filter((a) => a !== removed),
        suggestedApps: removed && removed.kind !== "executable" ? [...demoState.suggestedApps, removed] : demoState.suggestedApps,
      });
    }
    case "settings.save":
      demoState = {
        ...demoState,
        settings: {
          ...demoState.settings,
          baseUrl: String(params!.baseUrl ?? ""),
          platform: String(params!.platform ?? "pc"),
          ludusaviPath: String(params!.ludusaviPath ?? ""),
          apiKeySet: demoState.settings.apiKeySet || !!params!.apiKey,
          steamGridDbKeySet: demoState.settings.steamGridDbKeySet || !!params!.steamGridDbKey,
        },
      };
      return demoState;
    case "settings.startWithWindows":
      return (demoState = { ...demoState, settings: { ...demoState.settings, startWithWindows: !!params!.enabled } });
    case "update.check":
      // Pretend a new version comes down: checking, then ready to install.
      window.setTimeout(() => {
        demoState = { ...demoState, update: { kind: "ready", message: "Playdex 2.1.0 is ready. Restart to update.", version: "2.1.0" } };
        desktopState.set(demoState);
      }, 1600);
      return (demoState = { ...demoState, update: { kind: "checking", message: "Checking for updates…" } });
    case "art.search": {
      const title = String(params?.query || gameById(String(params?.libraryId))?.title || "");
      return {
        games: title
          ? [
              { id: 1, name: title, year: 2024 },
              { id: 2, name: `${title}: Deluxe Edition` },
            ]
          : [],
        picked: demoPicked.get(String(params?.libraryId)) ?? {},
      } satisfies ArtSearchView;
    }
    case "art.list":
      return demoArt(String(params?.libraryId), params?.kind as ArtKind);
    case "art.set":
    case "art.reset": {
      const libraryId = String(params?.libraryId);
      const kind = params?.kind as ArtKind;
      const images = { ...gameById(libraryId)?.images };
      const picked = { ...demoPicked.get(libraryId) };
      if (method === "art.set") {
        images[kind] = String(params?.url);
        picked[kind] = Number(params?.imageId);
      } else {
        delete images[kind];
        delete picked[kind];
      }
      demoPicked.set(libraryId, picked);
      return { images, picked } satisfies ArtView;
    }
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
