// Inside the Windows host the library is what the host found on this PC (installed Steam games, games added
// through the Playdex Wrapper, and the Xbox app's, Epic's and GOG's games), injected as window.__PLAYDEX_LIBRARY__ before this module runs. In a plain browser it
// is the design prototype's demo library: titles and artwork are real (Steam CDN), playtimes and sessions synthetic.

import { createStore, useStore } from "../nav/store";

export type Source = "steam" | "playdex" | "epic" | "gog" | "xbox" | "emulator" | "psn" | "switch";

/** Sources that run on this PC. Others are history Playdex tracked on another platform. */
export const PC_SOURCES: Source[] = ["steam", "playdex", "epic", "gog", "xbox", "emulator"];

export interface GameImages {
  hero?: string;
  logo?: string;
  portrait?: string;
  capsule?: string;
}

interface HostGame {
  id: string;
  title: string;
  /** "xbox", "epic", "gog": a game that launcher installed, not added to Playdex. */
  source: "steam" | "playdex" | "xbox" | "epic" | "gog";
  steamAppId?: number;
  /** Unix ms; Steam's own record of the last launch. */
  lastPlayed?: number;
  /** Artwork from Steam's local cache, served by the host. */
  images?: GameImages;
  /** Local games: how they are recorded to Playdex (provider and game id). */
  captureProvider?: string;
  captureId?: string;
}

declare global {
  interface Window {
    __PLAYDEX_LIBRARY__?: HostGame[];
  }
}

const hostLibrary = window.__PLAYDEX_LIBRARY__;

/** True when showing the demo library (plain browser), false when showing this PC's games. */
export const isDemo = !hostLibrary;

export interface Game {
  id: string;
  title: string;
  source: Source;
  /** Steam app id used only to fetch artwork. Absent = no artwork (fallback tile). */
  art?: number;
  /** Lifetime playtime in minutes, as Playdex reports it across platforms. */
  totalMinutes: number;
  /** PC games only: whether it is installed on this machine (default true). */
  installed?: boolean;
  /** Unix ms of the last launch as the store records it, for games with no Playdex sessions here. */
  lastPlayed?: number;
  /** Local artwork (host only); each missing kind falls back to the Steam CDN by `art`. */
  images?: GameImages;
  /** Local games (host only): the identity they are recorded to Playdex with; Steam games use `art`. */
  capture?: { provider: string; id: string };
}

export interface Session {
  gameId: string;
  /** 0 = today, 1 = yesterday, ... */
  dayOffset: number;
  /** Local start time, "HH:MM". */
  start: string;
  minutes: number;
}

export const SOURCE_LABEL: Record<Source, string> = {
  steam: "Steam",
  playdex: "Playdex",
  epic: "Epic Games",
  gog: "GOG",
  xbox: "Xbox",
  emulator: "RetroArch",
  psn: "PlayStation 5",
  switch: "Nintendo Switch",
};

export const isOnPc = (g: Game) => PC_SOURCES.includes(g.source);
export const isInstalled = (g: Game) => isOnPc(g) && g.installed !== false;

const demoGames: Game[] = [
  { id: "hades2", title: "Hades II", source: "steam", art: 1145350, totalMinutes: 3672 },
  { id: "balatro", title: "Balatro", source: "steam", art: 2379780, totalMinutes: 5410 },
  { id: "elden", title: "Elden Ring", source: "steam", art: 1245620, totalMinutes: 9845 },
  { id: "cyberpunk", title: "Cyberpunk 2077", source: "gog", art: 1091500, totalMinutes: 4920 },
  { id: "bg3", title: "Baldur's Gate 3", source: "steam", art: 1086940, totalMinutes: 11230 },
  { id: "starfield", title: "Starfield", source: "xbox", art: 1716740, totalMinutes: 1380 },
  { id: "celeste", title: "Celeste", source: "epic", art: 504230, totalMinutes: 1260 },
  { id: "oot", title: "The Legend of Zelda: Ocarina of Time", source: "emulator", totalMinutes: 845 },
  { id: "hollow", title: "Hollow Knight", source: "steam", art: 367520, totalMinutes: 3115 },
  { id: "tunic", title: "Tunic", source: "steam", art: 553420, totalMinutes: 960 },
  { id: "seaofstars", title: "Sea of Stars", source: "steam", art: 1244090, totalMinutes: 1735 },
  { id: "stardew", title: "Stardew Valley", source: "steam", art: 413150, totalMinutes: 7280 },
  { id: "spiderman", title: "Marvel's Spider-Man Remastered", source: "steam", art: 1817070, totalMinutes: 1520 },
  { id: "hogwarts", title: "Hogwarts Legacy", source: "epic", art: 990080, totalMinutes: 2210 },
  { id: "rdr2", title: "Red Dead Redemption 2", source: "steam", art: 1174180, totalMinutes: 6120 },
  { id: "witcher3", title: "The Witcher 3: Wild Hunt", source: "steam", art: 292030, totalMinutes: 8730 },
  { id: "skyrim", title: "The Elder Scrolls V: Skyrim", source: "steam", art: 489830, totalMinutes: 10440, installed: false },
  { id: "gow", title: "God of War", source: "steam", art: 1593500, totalMinutes: 2340 },
  { id: "sekiro", title: "Sekiro: Shadows Die Twice", source: "steam", art: 814380, totalMinutes: 3010 },
  { id: "ds3", title: "Dark Souls III", source: "steam", art: 374320, totalMinutes: 4560, installed: false },
  { id: "tlou", title: "The Last of Us Part I", source: "steam", art: 1888930, totalMinutes: 1290 },
  { id: "re4", title: "Resident Evil 4", source: "steam", art: 2050650, totalMinutes: 1180 },
  { id: "gtav", title: "Grand Theft Auto V", source: "epic", art: 271590, totalMinutes: 5230, installed: false },
  { id: "milesmorales", title: "Marvel's Spider-Man: Miles Morales", source: "steam", art: 1817190, totalMinutes: 640 },
  { id: "tsushima", title: "Ghost of Tsushima", source: "psn", art: 2215430, totalMinutes: 3870 },
  { id: "expedition33", title: "Clair Obscur: Expedition 33", source: "xbox", art: 1903340, totalMinutes: 2470 },
  { id: "palworld", title: "Palworld", source: "xbox", art: 1623730, totalMinutes: 1960, installed: false },
  { id: "terraria", title: "Terraria", source: "steam", art: 105600, totalMinutes: 6950 },
  { id: "sts", title: "Slay the Spire", source: "steam", art: 646570, totalMinutes: 4380 },
  { id: "cuphead", title: "Cuphead", source: "gog", art: 268910, totalMinutes: 870 },
  { id: "hades", title: "Hades", source: "steam", art: 1145360, totalMinutes: 5020 },
  { id: "valheim", title: "Valheim", source: "steam", art: 892970, totalMinutes: 3340, installed: false },
  { id: "horizonfw", title: "Horizon Forbidden West", source: "psn", art: 2420110, totalMinutes: 4110 },
  { id: "horizonzd", title: "Horizon Zero Dawn", source: "steam", art: 1151640, totalMinutes: 3290 },
  { id: "forza5", title: "Forza Horizon 5", source: "xbox", art: 1551360, totalMinutes: 2780 },
  { id: "halo", title: "Halo Infinite", source: "xbox", art: 1240440, totalMinutes: 1460, installed: false },
  { id: "deadcells", title: "Dead Cells", source: "switch", art: 588650, totalMinutes: 2210 },
  { id: "vampire", title: "Vampire Survivors", source: "steam", art: 1794680, totalMinutes: 1890 },
  { id: "diablo4", title: "Diablo IV", source: "steam", art: 2344520, totalMinutes: 3650, installed: false },
  { id: "factorio", title: "Factorio", source: "steam", art: 427520, totalMinutes: 12480 },
  { id: "lethal", title: "Lethal Company", source: "steam", art: 1966720, totalMinutes: 730 },
  { id: "mhwilds", title: "Monster Hunter Wilds", source: "steam", art: 2246340, totalMinutes: 2950 },
  { id: "mhworld", title: "Monster Hunter: World", source: "steam", art: 582010, totalMinutes: 7310, installed: false },
  { id: "mk8", title: "Mario Kart 8 Deluxe", source: "switch", totalMinutes: 1640 },
  { id: "astrobot", title: "Astro Bot", source: "psn", totalMinutes: 920 },
];

const demoSessions: Session[] = [
  { gameId: "hades2", dayOffset: 0, start: "21:02", minutes: 72 },
  { gameId: "balatro", dayOffset: 0, start: "13:40", minutes: 38 },
  { gameId: "celeste", dayOffset: 0, start: "08:15", minutes: 22 },
  { gameId: "hades2", dayOffset: 1, start: "22:10", minutes: 58 },
  { gameId: "balatro", dayOffset: 1, start: "19:05", minutes: 41 },
  { gameId: "elden", dayOffset: 2, start: "20:30", minutes: 125 },
  { gameId: "oot", dayOffset: 2, start: "17:45", minutes: 34 },
  { gameId: "hades2", dayOffset: 2, start: "12:20", minutes: 40 },
  { gameId: "bg3", dayOffset: 3, start: "15:00", minutes: 190 },
  { gameId: "balatro", dayOffset: 3, start: "23:10", minutes: 90 },
  { gameId: "cyberpunk", dayOffset: 4, start: "20:15", minutes: 160 },
  { gameId: "starfield", dayOffset: 5, start: "21:00", minutes: 75 },
  { gameId: "celeste", dayOffset: 5, start: "18:30", minutes: 25 },
  { gameId: "hollow", dayOffset: 7, start: "20:40", minutes: 95 },
  { gameId: "tunic", dayOffset: 8, start: "19:20", minutes: 64 },
  { gameId: "seaofstars", dayOffset: 9, start: "21:30", minutes: 82 },
  { gameId: "stardew", dayOffset: 11, start: "10:00", minutes: 140 },
  { gameId: "bg3", dayOffset: 12, start: "16:10", minutes: 210 },
  { gameId: "spiderman", dayOffset: 13, start: "20:00", minutes: 70 },
  { gameId: "hogwarts", dayOffset: 13, start: "14:30", minutes: 55 },
];

export const games: Game[] = hostLibrary
  ? hostLibrary.map((g) => ({
      id: g.id,
      title: g.title,
      source: g.source,
      art: g.steamAppId,
      totalMinutes: 0,
      lastPlayed: g.lastPlayed,
      images: g.images,
      capture: g.captureProvider && g.captureId ? { provider: g.captureProvider, id: g.captureId } : undefined,
    }))
  : demoGames;

/** Playdex history is not available to the launcher yet (the API key only reaches the capture routes). */
export const sessions: Session[] = hostLibrary ? [] : demoSessions;

const byId = new Map(games.map((g) => [g.id, g]));
export const gameById = (id: string): Game => byId.get(id)!;

/** Bumped when a game's artwork changes in place (picked on its page); the art components re-read it. */
export const artVersion = createStore(0);

/** A game's new local artwork, shown at once; the host has the library it loads next time hold it too. */
export function setImages(id: string, images: GameImages | undefined) {
  const game = byId.get(id);
  if (!game) return;
  game.images = images;
  artVersion.set(artVersion.get() + 1);
}

const steam = (id: number, file: string) => `https://cdn.cloudflare.steamstatic.com/steam/apps/${id}/${file}`;
export const art = {
  hero: (g: Game) => g.images?.hero ?? (g.art ? steam(g.art, "library_hero.jpg") : undefined),
  logo: (g: Game) => g.images?.logo ?? (g.art ? steam(g.art, "logo.png") : undefined),
  portrait: (g: Game) => g.images?.portrait ?? (g.art ? steam(g.art, "library_600x900.jpg") : undefined),
  capsule: (g: Game) => g.images?.capsule ?? (g.art ? steam(g.art, "capsule_616x353.jpg") : undefined),
};

/** Sessions sorted newest first (by day, then start time). */
export const sessionsNewestFirst = [...sessions].sort((a, b) => a.dayOffset - b.dayOffset || b.start.localeCompare(a.start));

export function sessionEnd(s: Session): string {
  const [h, m] = s.start.split(":").map(Number);
  const end = (h * 60 + m + s.minutes) % (24 * 60);
  return `${String(Math.floor(end / 60)).padStart(2, "0")}:${String(end % 60).padStart(2, "0")}`;
}

export interface GameStats {
  game: Game;
  /** Absent for games with no Playdex session here (the host library today). */
  lastSession?: Session;
  weekMinutes: number;
  weekSessions: number;
}

/** Games played since the page loaded (launched here, running, or in Playdex's records), newest last: they lead. */
const playedNow = new Map<string, number>();

/**
 * Games ordered by most recent session, with this-week totals (last 7 days). With no sessions (the host library),
 * every game, most recently launched first, then by title. Live: a game played since the page loaded moves to the
 * front (markPlayed), so the home screen follows switching between games without a restart.
 */
function computeRecent(): GameStats[] {
  if (!sessions.length) {
    return [...games]
      .sort((a, b) => (b.lastPlayed ?? 0) - (a.lastPlayed ?? 0) || a.title.localeCompare(b.title))
      .map((game) => ({ game, weekMinutes: 0, weekSessions: 0 }));
  }
  const fromSessions = statsFromSessions();
  const touched = fromSessions
    .filter((r) => playedNow.has(r.game.id))
    .sort((a, b) => playedNow.get(b.game.id)! - playedNow.get(a.game.id)!);
  return [...touched, ...fromSessions.filter((r) => !playedNow.has(r.game.id))];
}

function statsFromSessions(): GameStats[] {
  const seen = new Map<string, GameStats>();
  for (const s of sessionsNewestFirst) {
    let st = seen.get(s.gameId);
    if (!st) {
      st = { game: gameById(s.gameId), lastSession: s, weekMinutes: 0, weekSessions: 0 };
      seen.set(s.gameId, st);
    }
    if (s.dayOffset <= 6) {
      st.weekMinutes += s.minutes;
      st.weekSessions += 1;
    }
  }
  return [...seen.values()];
}

/** The recent games now; read it in event handlers, subscribe with useRecentGames in components. */
export let recentGames: GameStats[] = computeRecent();
const recentStore = createStore(recentGames);

/** The recent games, re-rendering when a game is played. */
export const useRecentGames = () => useStore(recentStore);

/**
 * Record that a game was played at `at` (now by default): when it started or ended here, when it is found running, or
 * Playdex's last session of it. Only ever moves a game's last-played time forward.
 */
export function markPlayed(gameId: string, at = Date.now()) {
  const game = byId.get(gameId);
  if (!game || !Number.isFinite(at) || (game.lastPlayed ?? 0) >= at) return;
  game.lastPlayed = at;
  playedNow.set(gameId, at);
  recentGames = computeRecent();
  recentStore.set(recentGames);
}

export const statsFor = (id: string): GameStats | undefined => recentGames.find((r) => r.game.id === id);

export const sessionsFor = (id: string) => sessionsNewestFirst.filter((s) => s.gameId === id);

/** Minutes per day for one game over the last `days` days, oldest first. */
export function dailyFor(id: string, days = 14): number[] {
  return Array.from({ length: days }, (_, i) =>
    sessions.filter((s) => s.gameId === id && s.dayOffset === days - 1 - i).reduce((t, s) => t + s.minutes, 0),
  );
}

/** Minutes per day for the last 7 days, oldest first (index 6 = today). */
export const weekByDay: number[] = Array.from({ length: 7 }, (_, i) =>
  sessions.filter((s) => s.dayOffset === 6 - i).reduce((t, s) => t + s.minutes, 0),
);

export const todayMinutes = weekByDay[6];
export const todaySessions = sessions.filter((s) => s.dayOffset === 0).length;
export const weekMinutes = weekByDay.reduce((a, b) => a + b, 0);

export function fmtMinutes(min: number): string {
  const h = Math.floor(min / 60);
  const m = min % 60;
  if (h === 0) return `${m}m`;
  if (m === 0) return `${h}h`;
  return `${h}h ${m}m`;
}

/** Lifetime hours; "—" when Playdex has no history for the game (the host library today). */
export function fmtHours(min: number): string {
  if (!min) return "—";
  return `${Math.round(min / 60).toLocaleString("en")}h`;
}

const weekday = new Intl.DateTimeFormat("en", { weekday: "short" });
const dayMonth = new Intl.DateTimeFormat("en", { day: "numeric", month: "short" });

export function dayLabel(offset: number): string {
  if (offset === 0) return "Today";
  if (offset === 1) return "Yesterday";
  const d = new Date();
  d.setDate(d.getDate() - offset);
  return offset < 7 ? weekday.format(d) : `${weekday.format(d)} ${dayMonth.format(d)}`;
}

/** Days since the game was last played (0 = today): its newest session, else the store's record. Undefined: never. */
export function lastPlayedDay(st: GameStats): number | undefined {
  if (st.lastSession) return st.lastSession.dayOffset;
  if (!st.game.lastPlayed) return undefined;
  const midnight = (t: number) => new Date(t).setHours(0, 0, 0, 0);
  return Math.max(0, Math.round((midnight(Date.now()) - midnight(st.game.lastPlayed)) / 86_400_000));
}

export function relativeDay(offset: number): string {
  if (offset === 0) return "Today";
  if (offset === 1) return "Yesterday";
  return `${offset} days ago`;
}

export function weekdayInitial(daysAgo: number): string {
  const d = new Date();
  d.setDate(d.getDate() - daysAgo);
  return weekday.format(d).slice(0, 1);
}

/** "Today", "3 days ago"…; "Installed" for a game with no record of being played. */
export function lastPlayedText(st: GameStats): string {
  const day = lastPlayedDay(st);
  return day === undefined ? "Installed" : relativeDay(day);
}
