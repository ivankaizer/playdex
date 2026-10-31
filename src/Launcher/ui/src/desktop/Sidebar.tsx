import { LayoutGrid, List, Plus, Search } from "lucide-react";
import { memo, useMemo, useState } from "react";
import { showToast, useIsRunning } from "../app-state";
import { Cover } from "../components/shared";
import { useFavorites } from "../data/favorites";
import { fmtHours, games, isInstalled, lastPlayedDay, useRecentGames, type Game } from "../data/games";
import { playdexSummary } from "../data/playdex";
import { rpc } from "../host/desktop";
import { useStore } from "../nav/store";
import { byTitle, deskTab, inScope, libraryView, openGamePage, selectedGame, totalMinutes, type Scope } from "./state";

/** The list's tabs: Recent, and the library's scopes. */
type Tab = "recent" | Scope;

const TABS: { id: Tab; label: string }[] = [
  { id: "all", label: "All" },
  { id: "recent", label: "Recent" },
  { id: "installed", label: "Installed" },
  { id: "favorites", label: "Favorites" },
];

/** Played within this many days counts as recent. */
const RECENT_DAYS = 14;

const EMPTY: Record<Tab, string> = {
  all: "Nothing here yet.",
  recent: `Nothing played in the last ${RECENT_DAYS} days.`,
  installed: "No games installed.",
  favorites: "No favorites yet.",
};

/**
 * The game list down the left, as in Steam's library: search, then All / Recent / Installed / Favorites, each one list
 * with every game once: Recent by when it was last played, the others A–Z. Picking one opens its page in the Library.
 */
export function Sidebar() {
  const [query, setQuery] = useState("");
  const [tab, setTab] = useState<Tab>("all");
  const recent = useRecentGames();
  const favorites = useFavorites();
  const view = useStore(libraryView);

  const lists = useMemo(() => {
    const played = recent
      .filter((r) => {
        const day = lastPlayedDay(r);
        return day !== undefined && day <= RECENT_DAYS;
      })
      .map((r) => r.game);
    const list = (id: Tab) => (id === "recent" ? played : games.filter((g) => inScope(id, g, favorites)).sort(byTitle));
    return Object.fromEntries(TABS.map((t) => [t.id, list(t.id)])) as Record<Tab, Game[]>;
  }, [favorites, recent]);

  const q = query.trim().toLowerCase();
  const shown = q ? lists[tab].filter((g) => g.title.toLowerCase().includes(q)) : lists[tab];

  return (
    <aside className="dk-side" aria-label="Games">
      <div className="dk-side-head">
        <div className="dk-side-tools">
          <label className="dk-search">
            <Search aria-hidden />
            <input
              type="search"
              placeholder={`Search ${lists[tab].length} games`}
              aria-label="Search games"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </label>
          <ViewToggle view={view} />
        </div>
        <div className="dk-seg" role="radiogroup" aria-label="Show" data-nav-group="side-scope">
          {TABS.map((t) => (
            <button key={t.id} role="radio" aria-checked={tab === t.id} data-nav data-kind="filter" onClick={() => setTab(t.id)}>
              {t.label}
            </button>
          ))}
        </div>
      </div>

      <div className="dk-side-list" data-scroll="y" data-nav-group="side-games">
        {shown.length === 0 && <p className="dk-side-empty">{q ? "No games match." : EMPTY[tab]}</p>}
        {shown.map((game) => (
          <Row key={game.id} game={game} />
        ))}
      </div>

      <div className="dk-side-foot">
        <button
          className="dk-add"
          data-nav
          data-kind="desk"
          onClick={() => rpc("game.add").catch((e: unknown) => showToast(e instanceof Error ? e.message : String(e)))}
        >
          <Plus aria-hidden /> Add a game
        </button>
      </div>
    </aside>
  );
}

/** List (the game's page beside this list) or grid (covers with filters and a details panel). */
export function ViewToggle({ view }: { view: "list" | "grid" }) {
  const pick = (v: "list" | "grid") => {
    libraryView.set(v);
    deskTab.set("Library");
  };
  return (
    <div className="dk-views" role="radiogroup" aria-label="Library view">
      <button role="radio" aria-checked={view === "list"} aria-label="List" title="List" onClick={() => pick("list")}>
        <List aria-hidden />
      </button>
      <button role="radio" aria-checked={view === "grid"} aria-label="Grid" title="Grid" onClick={() => pick("grid")}>
        <LayoutGrid aria-hidden />
      </button>
    </div>
  );
}

const Row = memo(function Row({ game }: { game: Game }) {
  const selectedId = useStore(selectedGame);
  const tab = useStore(deskTab);
  const selected = selectedId === game.id && tab === "Library";
  const playing = useIsRunning(game.id);
  const minutes = totalMinutes(useStore(playdexSummary), game);
  return (
    <button
      className="dk-row"
      aria-current={selected ? "page" : undefined}
      data-state={isInstalled(game) ? undefined : "off"}
      data-nav
      data-kind="cover"
      data-game={game.id}
      onClick={() => openGamePage(game.id)}
    >
      <Cover game={game} kind="portrait" className="dk-row-art" compact />
      <span className="dk-row-title">{game.title}</span>
      {playing ? (
        <span className="live-dot" role="img" aria-label="Playing now" />
      ) : (
        <span className="dk-row-meta num">{minutes ? fmtHours(minutes) : ""}</span>
      )}
    </button>
  );
});
