import { ChevronDown, LayoutGrid, List, Plus, Search } from "lucide-react";
import { memo, useMemo, useState } from "react";
import { showToast, useIsRunning } from "../app-state";
import { Cover } from "../components/shared";
import { useFavorites } from "../data/favorites";
import { fmtHours, games, isInstalled, lastPlayedDay, useRecentGames, type Game } from "../data/games";
import { playdexSummary } from "../data/playdex";
import { rpc } from "../host/desktop";
import { useStore } from "../nav/store";
import { byTitle, deskTab, inScope, libraryView, openGamePage, selectedGame, totalMinutes, type Scope } from "./state";

const SCOPES: { id: Scope; label: string }[] = [
  { id: "all", label: "All" },
  { id: "installed", label: "Installed" },
  { id: "favorites", label: "Favorites" },
];

/** Played within this many days counts as recent. */
const RECENT_DAYS = 14;
const RECENT_MAX = 6;

interface Group {
  id: string;
  label: string;
  games: Game[];
}

/**
 * The game list down the left, as in Steam's library: search, All / Installed / Favorites, and the games grouped as
 * Recent, Favorites and the rest A–Z. Picking one opens its page in the Library.
 */
export function Sidebar() {
  const [query, setQuery] = useState("");
  const [scope, setScope] = useState<Scope>("all");
  const [collapsed, setCollapsed] = useState<ReadonlySet<string>>(new Set());
  const recent = useRecentGames();
  const favorites = useFavorites();
  const view = useStore(libraryView);

  const counts = useMemo(
    () => Object.fromEntries(SCOPES.map((s) => [s.id, games.filter((g) => inScope(s.id, g, favorites)).length])) as Record<Scope, number>,
    [favorites],
  );

  const groups = useMemo<Group[]>(() => {
    const visible = games.filter((g) => inScope(scope, g, favorites));
    const q = query.trim().toLowerCase();
    if (q) return [{ id: "results", label: "Results", games: visible.filter((g) => g.title.toLowerCase().includes(q)).sort(byTitle) }];
    const recentGames = recent
      .filter((r) => {
        const day = lastPlayedDay(r);
        return day !== undefined && day <= RECENT_DAYS && inScope(scope, r.game, favorites);
      })
      .slice(0, RECENT_MAX)
      .map((r) => r.game);
    return [
      { id: "recent", label: "Recent", games: recentGames },
      { id: "favorites", label: "Favorites", games: scope === "favorites" ? [] : visible.filter((g) => favorites.has(g.id)).sort(byTitle) },
      { id: "all", label: scope === "installed" ? "Installed" : scope === "favorites" ? "Favorites" : "All games", games: [...visible].sort(byTitle) },
    ].filter((g) => g.games.length > 0);
  }, [query, scope, favorites, recent]);

  const toggle = (id: string) =>
    setCollapsed((c) => {
      const next = new Set(c);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });

  return (
    <aside className="dk-side" aria-label="Games">
      <div className="dk-side-head">
        <div className="dk-side-tools">
          <label className="dk-search">
            <Search aria-hidden />
            <input
              type="search"
              placeholder={`Search ${games.length} games`}
              aria-label="Search games"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </label>
          <ViewToggle view={view} />
        </div>
        <div className="dk-seg" role="radiogroup" aria-label="Show" data-nav-group="side-scope">
          {SCOPES.map((s) => (
            <button key={s.id} role="radio" aria-checked={scope === s.id} data-nav data-kind="filter" onClick={() => setScope(s.id)}>
              {s.label} <span className="num">{counts[s.id]}</span>
            </button>
          ))}
        </div>
      </div>

      <div className="dk-side-list" data-scroll="y" data-nav-group="side-games">
        {groups.length === 0 && <p className="dk-side-empty">{query ? "No games match." : "Nothing here yet."}</p>}
        {groups.map((g) => (
          <section key={g.id} className="dk-group" aria-label={g.label}>
            <button className="dk-group-head" aria-expanded={!collapsed.has(g.id)} onClick={() => toggle(g.id)}>
              <ChevronDown aria-hidden />
              <span>{g.label}</span>
              <span className="num">{g.games.length}</span>
            </button>
            {!collapsed.has(g.id) && g.games.map((game) => <Row key={game.id} game={game} />)}
          </section>
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
      {playing ? <span className="live-dot" role="img" aria-label="Playing now" /> : <span className="dk-row-meta num">{minutes ? fmtHours(minutes) : ""}</span>}
    </button>
  );
});
