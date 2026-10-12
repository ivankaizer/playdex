/*
 * The user's Playdex data, as the host fetches it from GET /api/launcher/summary with the install's API key. The last
 * summary is injected as window.__PLAYDEX_SUMMARY__ before this module runs (null before the first one, or without a
 * key) and the host posts {type:"playdex-summary"} when a newer one arrives. In a plain browser a summary is made from
 * the demo sessions.
 */
import { inHost, onHostMessage } from "../host/bridge";
import { createStore } from "../nav/store";
import { gameById, games, isDemo, sessions, SOURCE_LABEL, type Game } from "./games";

export type PlaydexPeriod = { totalSeconds: number; sessionCount: number };
export type PlaydexDay = { date: string; totalSeconds: number; sessionCount: number };
export type PlaydexTopGame = { gameId: string; title: string; totalSeconds: number; sessionCount: number };
export type PlaydexSession = {
  sessionId: string;
  gameId: string;
  title: string;
  platformKey: string;
  /** The platform's name from Playdex's catalog; absent from servers older than the field. */
  platformName?: string;
  startedAt: string;
  endedAt: string | null;
  durationSeconds: number;
};
export type PlaydexIdentity = { type: string; externalId: string };
export type PlaydexGame = {
  gameId: string;
  title: string;
  capsuleUrl: string | null;
  totalSeconds: number;
  sessionCount: number;
  lastPlayedAt: string;
  identities: PlaydexIdentity[];
};
export type PlaydexSummary = {
  timeZone: string;
  generatedAt: string;
  today: PlaydexPeriod;
  week: PlaydexPeriod;
  allTime: PlaydexPeriod;
  currentStreakDays: number;
  /** The last 7 local days, oldest first; the last is today. */
  days: PlaydexDay[];
  topGames: PlaydexTopGame[];
  recentSessions: PlaydexSession[];
  games: PlaydexGame[];
};

declare global {
  interface Window {
    __PLAYDEX_SUMMARY__?: PlaydexSummary | null;
    /** Whether this install has a Playdex API key. */
    __PLAYDEX_API__?: boolean;
  }
}

/** True when the host has a Playdex API key (always in a plain browser, which shows the demo). */
export const playdexConnected = !inHost || window.__PLAYDEX_API__ === true;

export const playdexSummary = createStore<PlaydexSummary | null>(
  inHost ? (window.__PLAYDEX_SUMMARY__ ?? null) : isDemo ? demoSummary() : null,
);

onHostMessage((m) => {
  if (m.type === "playdex-summary") playdexSummary.set(m.summary as PlaydexSummary | null);
});

/** "steam:3669870": how a game is recorded to Playdex, the key a library game and a Playdex game share. */
const identityKey = (type: string, id: string) => `${type.toLowerCase()}:${id}`;

/** The library game's Playdex record key: its Steam app id, or the identity a local game is captured with. */
function captureKey(g: Game): string | undefined {
  if (g.capture) return identityKey(g.capture.provider, g.capture.id);
  if (g.source === "steam" && g.art) return identityKey("steam", String(g.art));
  return undefined;
}

let indexed: { summary: PlaydexSummary | null; byKey: Map<string, PlaydexGame> } = { summary: null, byKey: new Map() };

/** The library game's Playdex totals in `summary`, if Playdex has a session of it. */
export function playdexGame(summary: PlaydexSummary | null, g: Game): PlaydexGame | undefined {
  if (!summary) return undefined;
  if (indexed.summary !== summary) {
    const byKey = new Map<string, PlaydexGame>();
    for (const pg of summary.games) for (const i of pg.identities) byKey.set(identityKey(i.type, i.externalId), pg);
    indexed = { summary, byKey };
  }
  const key = captureKey(g);
  return key ? indexed.byKey.get(key) : undefined;
}

let reverse: { summary: PlaydexSummary | null; byPlaydexId: Map<string, Game> } = { summary: null, byPlaydexId: new Map() };

/** The library game a Playdex game is, when it is on this PC (installed, or added to Playdex here). */
export function libraryGame(summary: PlaydexSummary | null, playdexGameId: string): Game | undefined {
  if (!summary) return undefined;
  if (reverse.summary !== summary) {
    const byKey = new Map<string, Game>();
    for (const g of games) {
      const key = captureKey(g);
      if (key) byKey.set(key, g);
    }
    const byPlaydexId = new Map<string, Game>();
    for (const pg of summary.games) {
      const local = pg.identities.map((i) => byKey.get(identityKey(i.type, i.externalId))).find(Boolean);
      if (local) byPlaydexId.set(pg.gameId, local);
    }
    reverse = { summary, byPlaydexId };
  }
  return reverse.byPlaydexId.get(playdexGameId);
}

/** Days since an ISO time, in local calendar days (0 = today). */
export function daysAgo(iso: string): number {
  const midnight = (t: number) => new Date(t).setHours(0, 0, 0, 0);
  return Math.max(0, Math.round((midnight(Date.now()) - midnight(Date.parse(iso))) / 86_400_000));
}

/* ---------- Demo: the prototype's sessions as a summary ---------- */

function demoSummary(): PlaydexSummary {
  const now = new Date();
  const at = (dayOffset: number, start: string) => {
    const d = new Date(now);
    d.setDate(d.getDate() - dayOffset);
    const [h, m] = start.split(":").map(Number);
    d.setHours(h, m, 0, 0);
    return d;
  };
  const rows = sessions.map((s, i) => {
    const startedAt = at(s.dayOffset, s.start);
    return {
      sessionId: String(i + 1),
      gameId: s.gameId,
      title: gameById(s.gameId).title,
      platformKey: gameById(s.gameId).source,
      platformName: SOURCE_LABEL[gameById(s.gameId).source],
      startedAt: startedAt.toISOString(),
      endedAt: new Date(startedAt.getTime() + s.minutes * 60_000).toISOString(),
      durationSeconds: s.minutes * 60,
      dayOffset: s.dayOffset,
    };
  });
  const period = (list: typeof rows): PlaydexPeriod => ({
    totalSeconds: list.reduce((t, r) => t + r.durationSeconds, 0),
    sessionCount: list.length,
  });
  const week = rows.filter((r) => r.dayOffset <= 6);
  const byGame = new Map<string, typeof rows>();
  for (const r of rows) byGame.set(r.gameId, [...(byGame.get(r.gameId) ?? []), r]);
  const weekByGame = new Map<string, typeof rows>();
  for (const r of week) weekByGame.set(r.gameId, [...(weekByGame.get(r.gameId) ?? []), r]);
  const played = new Set(rows.map((r) => r.dayOffset));
  let streak = 0;
  for (let d = played.has(0) ? 0 : 1; played.has(d); d++) streak++;

  return {
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    generatedAt: now.toISOString(),
    today: period(rows.filter((r) => r.dayOffset === 0)),
    week: period(week),
    allTime: period(rows),
    currentStreakDays: streak,
    days: Array.from({ length: 7 }, (_, i) => {
      const offset = 6 - i;
      const list = rows.filter((r) => r.dayOffset === offset);
      return { date: at(offset, "12:00").toISOString().slice(0, 10), ...period(list) };
    }),
    topGames: [...weekByGame.entries()]
      .map(([gameId, list]) => ({ gameId, title: gameById(gameId).title, ...period(list) }))
      .sort((a, b) => b.totalSeconds - a.totalSeconds)
      .slice(0, 5),
    recentSessions: [...rows].sort((a, b) => b.startedAt.localeCompare(a.startedAt)).slice(0, 20),
    games: games
      .filter((g) => byGame.has(g.id))
      .map((g) => {
        const list = byGame.get(g.id)!;
        return {
          gameId: g.id,
          title: g.title,
          capsuleUrl: null,
          ...period(list),
          totalSeconds: g.totalMinutes * 60,
          lastPlayedAt: list.map((r) => r.startedAt).sort().at(-1)!,
          identities: g.art ? [{ type: "steam", externalId: String(g.art) }] : [],
        };
      }),
  };
}
