import { gameById, isDemo, isInstalled, recentGames, type Game } from "../data/games";
import { playdexGame, type PlaydexSummary } from "../data/playdex";
import { createStore, type Store } from "../nav/store";

/*
 * Where desktop mode is: the open tab, the game the Library shows, and how the Library lists games. The desktop app
 * reloads the page after games change, so the tab and the game survive that (session storage); the Library's view is
 * a preference (local storage).
 */
export const DESK_TABS = ["Home", "Library", "Playdex", "Manage", "Settings"] as const;
export type DeskTab = (typeof DESK_TABS)[number];

export type LibraryView = "list" | "grid";

function read(storage: () => Storage, key: string): string | null {
  try {
    return storage().getItem(key);
  } catch {
    return null;
  }
}

function persisted<T extends string | null>(store: Store<T>, storage: () => Storage, key: string) {
  store.subscribe(() => {
    try {
      const v = store.get();
      if (v === null) storage().removeItem(key);
      else storage().setItem(key, v);
    } catch {
      // Not remembered; still applies now.
    }
  });
  return store;
}

const savedTab = read(() => sessionStorage, "pdx.tab");
export const deskTab = persisted(
  createStore<DeskTab>(DESK_TABS.includes(savedTab as DeskTab) ? (savedTab as DeskTab) : "Home"),
  () => sessionStorage,
  "pdx.tab",
);

const savedGame = read(() => sessionStorage, "pdx.game");
/** The game the Library shows: its page in the list view, its panel in the grid. The last played until one is picked. */
export const selectedGame = persisted(
  createStore<string | null>(savedGame && gameById(savedGame) ? savedGame : (recentGames[0]?.game.id ?? null)),
  () => sessionStorage,
  "pdx.game",
);

export const libraryView = persisted(
  createStore<LibraryView>(read(() => localStorage, "pdx.libraryView") === "grid" ? "grid" : "list"),
  () => localStorage,
  "pdx.libraryView",
);

export function goTo(tab: DeskTab) {
  deskTab.set(tab);
}

/** The game's page: the Library's list view with the game selected. */
export function openGamePage(gameId: string) {
  selectedGame.set(gameId);
  libraryView.set("list");
  deskTab.set("Library");
}

/** Lifetime minutes as Playdex has them (the demo's own figures in a browser); 0 without a Playdex record. */
export function totalMinutes(summary: PlaydexSummary | null, game: Game): number {
  if (isDemo) return game.totalMinutes;
  const played = playdexGame(summary, game);
  return played ? Math.round(played.totalSeconds / 60) : 0;
}

export type Scope = "all" | "installed" | "favorites";

export function inScope(scope: Scope, game: Game, favorites: ReadonlySet<string>): boolean {
  if (scope === "installed") return isInstalled(game);
  if (scope === "favorites") return favorites.has(game.id);
  return true;
}

export const byTitle = (a: Game, b: Game) => a.title.localeCompare(b.title, "en", { sensitivity: "base" });
