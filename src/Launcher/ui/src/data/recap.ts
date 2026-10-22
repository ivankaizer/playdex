/*
 * A year's Playdex recap: the web's Recap page numbers for one calendar year (GET /api/launcher/recap?year=&tz=, which
 * runs the same RecapService in this page's time zone), fetched by the host with the install's API key when the Playdex
 * tab shows it. In a plain browser a recap is made from the demo sessions.
 */
import { inHost, onHostMessage, postToHost } from "../host/bridge";
import { createStore } from "../nav/store";
import { gameById, sessions, SOURCE_LABEL } from "./games";

export type RecapGame = {
  gameId: string;
  title: string;
  capsuleUrl: string | null;
  totalSeconds: number;
  sessionCount: number;
  averageSessionSeconds: number;
};

/** The fields of the web's RecapResponse the launcher shows. Weekday 0 is Monday; month is "yyyy-MM". */
export type Recap = {
  totalSessions: number;
  totalSeconds: number;
  averageSessionSeconds: number;
  longestStreakDays: number;
  daysPlayed: number;
  daysNotPlayed: number;
  mostActiveMonth: { month: string; sessionCount: number } | null;
  sessionsByWeekday: { weekday: number; sessionCount: number }[];
  sessionsByHour: { hour: number; sessionCount: number }[];
  peakHour: { hour: number; sessionCount: number } | null;
  weekendSeconds: number;
  weekdaySeconds: number;
  uniqueGames: number;
  newGamesTried: number;
  topGamesByTime: RecapGame[];
  gameWithLongestSession: { gameId: string; title: string; sessionSeconds: number } | null;
  mostBingedGame: { gameId: string; title: string; sessionCount: number } | null;
  platformTotals: { platformKey: string; platformName: string; sessionCount: number; totalSeconds: number }[];
};

export type RecapState = { status: "loading" } | { status: "ready"; recap: Recap } | { status: "unavailable" };

/** By year. A year shown before keeps its recap while it is fetched again. */
export const recaps = createStore<Record<number, RecapState>>({});

function put(year: number, state: RecapState) {
  recaps.set({ ...recaps.get(), [year]: state });
}

/**
 * This page's IANA time zone (e.g. "Europe/Warsaw"), as the web app sends it: the API counts the recap's year, days and
 * hours in it, so they match the calendar shown here. UTC when the browser cannot tell.
 */
export function clientTimeZone(): string {
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC";
  } catch {
    return "UTC";
  }
}

/** Fetch a year's recap (again); the store says when it is in. */
export function loadRecap(year: number) {
  if (recaps.get()[year]?.status !== "ready") put(year, { status: "loading" });
  if (inHost) {
    postToHost({ type: "playdex-recap", year, timeZone: clientTimeZone() });
  } else {
    window.setTimeout(() => put(year, { status: "ready", recap: demoRecap(year) }), 250);
  }
}

onHostMessage((m) => {
  if (m.type !== "playdex-recap") return;
  if (m.recap) put(m.year, { status: "ready", recap: m.recap as Recap });
  else if (recaps.get()[m.year]?.status !== "ready") put(m.year, { status: "unavailable" });
});

/* ---------- Demo: the prototype's sessions as this year's recap, a lighter last year ---------- */

function demoRecap(year: number): Recap {
  const scale = year === new Date().getFullYear() ? 1 : 0.6;
  const rows = sessions.map((s) => ({ ...s, seconds: Math.round(s.minutes * 60 * scale), game: gameById(s.gameId) }));
  const total = rows.reduce((t, r) => t + r.seconds, 0);
  const byGame = new Map<string, typeof rows>();
  for (const r of rows) byGame.set(r.gameId, [...(byGame.get(r.gameId) ?? []), r]);
  const games = [...byGame.entries()]
    .map(([gameId, list]) => {
      const seconds = list.reduce((t, r) => t + r.seconds, 0);
      return {
        gameId,
        title: list[0].game.title,
        capsuleUrl: null,
        totalSeconds: seconds,
        sessionCount: list.length,
        averageSessionSeconds: seconds / list.length,
      };
    })
    .sort((a, b) => b.totalSeconds - a.totalSeconds);
  const byPlatform = new Map<string, typeof rows>();
  for (const r of rows) byPlatform.set(r.game.source, [...(byPlatform.get(r.game.source) ?? []), r]);
  const longest = [...rows].sort((a, b) => b.seconds - a.seconds)[0];
  const hours = Array.from({ length: 24 }, (_, hour) => ({
    hour,
    sessionCount: rows.filter((r) => Number(r.start.split(":")[0]) === hour).length,
  }));
  const peak = [...hours].sort((a, b) => b.sessionCount - a.sessionCount)[0];
  const days = new Set(rows.map((r) => r.dayOffset)).size;
  return {
    totalSessions: rows.length,
    totalSeconds: total,
    averageSessionSeconds: rows.length ? total / rows.length : 0,
    longestStreakDays: Math.min(days, 6),
    daysPlayed: days,
    daysNotPlayed: 365 - days,
    mostActiveMonth: { month: `${year}-${scale === 1 ? "10" : "03"}`, sessionCount: rows.length },
    sessionsByWeekday: Array.from({ length: 7 }, (_, weekday) => ({ weekday, sessionCount: (weekday * 3 + 2) % 7 })),
    sessionsByHour: hours,
    peakHour: peak.sessionCount ? peak : null,
    weekendSeconds: Math.round(total * 0.4),
    weekdaySeconds: Math.round(total * 0.6),
    uniqueGames: games.length,
    newGamesTried: Math.max(1, Math.round(games.length / 2)),
    topGamesByTime: games.slice(0, 5),
    gameWithLongestSession: longest ? { gameId: longest.gameId, title: longest.game.title, sessionSeconds: longest.seconds } : null,
    mostBingedGame: games[0] ? { gameId: games[0].gameId, title: games[0].title, sessionCount: games[0].sessionCount } : null,
    platformTotals: [...byPlatform.entries()].map(([key, list]) => ({
      platformKey: key,
      platformName: SOURCE_LABEL[list[0].game.source],
      sessionCount: list.length,
      totalSeconds: list.reduce((t, r) => t + r.seconds, 0),
    })),
  };
}
