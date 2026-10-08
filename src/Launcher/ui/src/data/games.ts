// Demo library for the design prototype. Titles and artwork are real (Steam CDN);
// playtimes and sessions are synthetic sample data.

export type Source = "steam" | "epic" | "gog" | "xbox" | "emulator" | "psn" | "switch";

/** Sources that run on this PC. Others are history Playdex tracked on another platform. */
export const PC_SOURCES: Source[] = ["steam", "epic", "gog", "xbox", "emulator"];

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
  epic: "Epic Games",
  gog: "GOG",
  xbox: "PC Game Pass",
  emulator: "RetroArch",
  psn: "PlayStation 5",
  switch: "Nintendo Switch",
};

export const isOnPc = (g: Game) => PC_SOURCES.includes(g.source);
export const isInstalled = (g: Game) => isOnPc(g) && g.installed !== false;

export const games: Game[] = [
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
  { id: "witcher3", title: "The Witcher 3: Wild Hunt", source: "gog", art: 292030, totalMinutes: 8730 },
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

export const sessions: Session[] = [
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

const byId = new Map(games.map((g) => [g.id, g]));
export const gameById = (id: string): Game => byId.get(id)!;

const steam = (id: number, file: string) => `https://cdn.cloudflare.steamstatic.com/steam/apps/${id}/${file}`;
export const art = {
  hero: (g: Game) => (g.art ? steam(g.art, "library_hero.jpg") : undefined),
  logo: (g: Game) => (g.art ? steam(g.art, "logo.png") : undefined),
  portrait: (g: Game) => (g.art ? steam(g.art, "library_600x900.jpg") : undefined),
  capsule: (g: Game) => (g.art ? steam(g.art, "capsule_616x353.jpg") : undefined),
};

/** Sessions sorted newest first (by day, then start time). */
export const sessionsNewestFirst = [...sessions].sort(
  (a, b) => a.dayOffset - b.dayOffset || b.start.localeCompare(a.start),
);

export function sessionEnd(s: Session): string {
  const [h, m] = s.start.split(":").map(Number);
  const end = (h * 60 + m + s.minutes) % (24 * 60);
  return `${String(Math.floor(end / 60)).padStart(2, "0")}:${String(end % 60).padStart(2, "0")}`;
}

export interface GameStats {
  game: Game;
  lastSession: Session;
  weekMinutes: number;
  weekSessions: number;
}

/** Games ordered by most recent session, with this-week totals (last 7 days). */
export const recentGames: GameStats[] = (() => {
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
})();

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

export function fmtHours(min: number): string {
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
