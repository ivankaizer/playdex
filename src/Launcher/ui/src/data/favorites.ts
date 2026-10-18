import { createStore, useStore } from "../nav/store";
import { isDemo } from "./games";

/*
 * Favorite games, by library id, kept in this browser profile. Both modes share them: the game page toggles one, and
 * desktop mode lists them in the sidebar. The demo library starts with two.
 */
const KEY = "pdx.favorites";

function initial(): ReadonlySet<string> {
  try {
    const saved = localStorage.getItem(KEY);
    if (saved) return new Set(JSON.parse(saved) as string[]);
  } catch {
    // Storage blocked or unreadable: none for this session.
  }
  return new Set(isDemo ? ["hades2", "elden"] : []);
}

const favorites = createStore<ReadonlySet<string>>(initial());

export const useFavorites = () => useStore(favorites);

export function toggleFavorite(gameId: string) {
  const next = new Set(favorites.get());
  if (next.has(gameId)) next.delete(gameId);
  else next.add(gameId);
  favorites.set(next);
  try {
    localStorage.setItem(KEY, JSON.stringify([...next]));
  } catch {
    // Not remembered; still applies now.
  }
}
