import { ArrowUpRight, Download, MonitorSmartphone, Search } from "lucide-react";
import { memo, useMemo, useState } from "react";
import { useIsRunning } from "../app-state";
import { SaveGlyph, useSaves } from "../components/saves";
import { Cover, Logo } from "../components/shared";
import { useFavorites } from "../data/favorites";
import {
  dayLabel,
  fmtHours,
  fmtMinutes,
  gameById,
  games,
  isInstalled,
  isOnPc,
  lastPlayedDay,
  sessionEnd,
  SOURCE_LABEL,
  useRecentGames,
  type Game,
  type GameStats,
  type Source,
} from "../data/games";
import { gameHistory, playdexSummary, type PlaydexSummary } from "../data/playdex";
import { useStore } from "../nav/store";
import { PlayButton, Stat } from "./parts";
import { ViewToggle } from "./Sidebar";
import { byTitle, openGamePage, selectedGame, totalMinutes } from "./state";

type Status = "installed" | "missing" | "elsewhere";
type Played = "recent" | "never";

const statusOf = (g: Game): Status => (isInstalled(g) ? "installed" : isOnPc(g) ? "missing" : "elsewhere");
const STATUS_LABEL: Record<Status, string> = { installed: "Installed", missing: "Not installed", elsewhere: "Other platforms" };

/** Played within this many days counts as played recently. */
const RECENT_DAYS = 30;

const SORTS = [
  { id: "recent", label: "Recently played" },
  { id: "az", label: "Name" },
  { id: "most", label: "Most played" },
] as const;
type SortId = (typeof SORTS)[number]["id"];

/** Cover widths in px for the size slider's five steps. */
const SIZES = [108, 128, 148, 172, 200];

function sorter(sort: SortId, summary: PlaydexSummary | null, recent: GameStats[]) {
  if (sort === "az") return byTitle;
  if (sort === "most") return (a: Game, b: Game) => totalMinutes(summary, b) - totalMinutes(summary, a) || byTitle(a, b);
  const rank = new Map(recent.map((r, i) => [r.game.id, i]));
  return (a: Game, b: Game) => (rank.get(a.id) ?? 1e9) - (rank.get(b.id) ?? 1e9) || byTitle(a, b);
}

/** Toggle one value in a set kept in state; an empty set means "any". */
function useToggleSet<T>() {
  const [set, setSet] = useState<ReadonlySet<T>>(new Set());
  const toggle = (v: T) =>
    setSet((s) => {
      const next = new Set(s);
      if (next.has(v)) next.delete(v);
      else next.add(v);
      return next;
    });
  return [set, toggle, () => setSet(new Set())] as const;
}

/**
 * The Library as covers, as in Playnite: filters down the left (source, status, when played, favorites), the covers
 * with search, sort and size, and the picked game's panel on the right with Play.
 */
export function LibraryGrid() {
  const [query, setQuery] = useState("");
  const [sources, toggleSource, clearSources] = useToggleSet<Source>();
  const [statuses, toggleStatus, clearStatuses] = useToggleSet<Status>();
  const [played, togglePlayed, clearPlayed] = useToggleSet<Played>();
  const [favoritesOnly, setFavoritesOnly] = useState(false);
  const [sort, setSort] = useState<SortId>("recent");
  const [size, setSize] = useState(2);
  const summary = useStore(playdexSummary);
  const recent = useRecentGames();
  const favorites = useFavorites();
  const selected = useStore(selectedGame);

  const sourceCounts = useMemo(() => {
    const m = new Map<Source, number>();
    for (const g of games) m.set(g.source, (m.get(g.source) ?? 0) + 1);
    return [...m.entries()].sort((a, b) => b[1] - a[1]);
  }, []);
  const statusCounts = useMemo(() => {
    const m = new Map<Status, number>();
    for (const g of games) m.set(statusOf(g), (m.get(statusOf(g)) ?? 0) + 1);
    return (["installed", "missing", "elsewhere"] as Status[]).filter((s) => m.has(s)).map((s) => [s, m.get(s)!] as const);
  }, []);
  const playedDay = useMemo(() => new Map(recent.map((r) => [r.game.id, lastPlayedDay(r)])), [recent]);

  const list = useMemo(() => {
    const q = query.trim().toLowerCase();
    return games
      .filter((g) => !q || g.title.toLowerCase().includes(q))
      .filter((g) => sources.size === 0 || sources.has(g.source))
      .filter((g) => statuses.size === 0 || statuses.has(statusOf(g)))
      .filter((g) => !favoritesOnly || favorites.has(g.id))
      .filter((g) => {
        if (played.size === 0) return true;
        const day = playedDay.get(g.id);
        return (
          (played.has("recent") && day !== undefined && day <= RECENT_DAYS) ||
          (played.has("never") && day === undefined && !totalMinutes(summary, g))
        );
      })
      .sort(sorter(sort, summary, recent));
  }, [query, sources, statuses, played, favoritesOnly, favorites, sort, summary, recent, playedDay]);

  const filtered = sources.size + statuses.size + played.size > 0 || favoritesOnly || query.trim() !== "";
  const clear = () => {
    clearSources();
    clearStatuses();
    clearPlayed();
    setFavoritesOnly(false);
    setQuery("");
  };
  const shown = selected && gameById(selected) ? selected : list[0]?.id;

  return (
    <div className="dk-grid-view">
      <aside className="dk-filters" aria-label="Filters">
        <label className="dk-search">
          <Search aria-hidden />
          <input type="search" placeholder="Search" aria-label="Search games" value={query} onChange={(e) => setQuery(e.target.value)} />
        </label>
        <fieldset>
          <legend className="dk-label">Source</legend>
          {sourceCounts.map(([s, n]) => (
            <Check key={s} label={SOURCE_LABEL[s]} count={n} checked={sources.has(s)} onChange={() => toggleSource(s)} />
          ))}
        </fieldset>
        <fieldset>
          <legend className="dk-label">Status</legend>
          {statusCounts.map(([s, n]) => (
            <Check key={s} label={STATUS_LABEL[s]} count={n} checked={statuses.has(s)} onChange={() => toggleStatus(s)} />
          ))}
          <Check label="Favorites" count={favorites.size} checked={favoritesOnly} onChange={() => setFavoritesOnly((f) => !f)} />
        </fieldset>
        <fieldset>
          <legend className="dk-label">Played</legend>
          <Check label="In the last 30 days" checked={played.has("recent")} onChange={() => togglePlayed("recent")} />
          <Check label="Never" checked={played.has("never")} onChange={() => togglePlayed("never")} />
        </fieldset>
        {filtered && (
          <button className="dk-link" onClick={clear}>
            Clear filters
          </button>
        )}
      </aside>

      <section className="dk-grid-main" aria-label="Games">
        <div className="dk-grid-bar">
          <h1>{filtered ? "Filtered" : "All games"}</h1>
          <span className="dk-muted num">{filtered ? `${list.length} of ${games.length}` : `${games.length}`} games</span>
          <span className="dk-spacer" />
          <label className="dk-select">
            Sort
            <select value={sort} onChange={(e) => setSort(e.target.value as SortId)}>
              {SORTS.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.label}
                </option>
              ))}
            </select>
          </label>
          <ViewToggle view="grid" />
          <input
            type="range"
            className="dk-range"
            min={0}
            max={SIZES.length - 1}
            value={size}
            aria-label="Cover size"
            onChange={(e) => setSize(Number(e.target.value))}
          />
        </div>
        <div
          className="dk-covers"
          data-scroll="y"
          data-nav-group="grid"
          style={{ "--dk-cover": `${SIZES[size]}px` } as React.CSSProperties}
        >
          {list.length === 0 ? (
            <p className="dk-muted">No games match these filters.</p>
          ) : (
            list.map((g) => <CoverItem key={g.id} game={g} selected={g.id === shown} />)
          )}
        </div>
      </section>

      {shown && <Details key={shown} gameId={shown} />}
    </div>
  );
}

function Check({ label, count, checked, onChange }: { label: string; count?: number; checked: boolean; onChange: () => void }) {
  return (
    <label className="dk-check">
      <input type="checkbox" checked={checked} onChange={onChange} />
      <span>{label}</span>
      {count !== undefined && <span className="dk-muted num">{count}</span>}
    </label>
  );
}

const CoverItem = memo(function CoverItem({ game, selected }: { game: Game; selected: boolean }) {
  const playing = useIsRunning(game.id);
  const minutes = totalMinutes(useStore(playdexSummary), game);
  const saves = useSaves(game.id)?.status;
  const status = statusOf(game);
  return (
    <button
      className="dk-cover"
      aria-pressed={selected}
      data-state={status === "installed" ? undefined : "off"}
      data-nav
      data-kind={status === "installed" ? "cover" : "cover-off"}
      data-game={game.id}
      title="Double-click to open the game's page"
      onClick={() => selectedGame.set(game.id)}
      onDoubleClick={() => openGamePage(game.id)}
    >
      <span className="dk-cover-art">
        <Cover game={game} kind="portrait" />
        {playing && (
          <span className="dk-cover-playing">
            <span className="live-dot" /> Playing
          </span>
        )}
        {status !== "installed" && (
          <span className="dk-cover-badge" title={status === "missing" ? "Not installed" : `Played on ${SOURCE_LABEL[game.source]}`}>
            {status === "missing" ? <Download aria-hidden /> : <MonitorSmartphone aria-hidden />}
          </span>
        )}
        {saves && (
          <span className="dk-cover-save" title={saves.label}>
            <SaveGlyph kind={saves.kind} />
          </span>
        )}
      </span>
      <span className="dk-cover-title">{game.title}</span>
      <span className="dk-cover-sub num">
        {minutes ? `${fmtHours(minutes)} · ` : ""}
        {SOURCE_LABEL[game.source]}
      </span>
    </button>
  );
});

/** The picked game: its art, Play, the headline figures and its last sessions. */
function Details({ gameId }: { gameId: string }) {
  const game = gameById(gameId);
  const history = gameHistory(useStore(playdexSummary), game);
  const saves = useSaves(gameId)?.status;
  const status = statusOf(game);
  const lastDay = history.lastPlayedDay;
  return (
    <aside className="dk-details" aria-label={game.title}>
      <div className="dk-details-art">
        <Cover game={game} kind="hero" className="dk-card-art" />
        <div className="dk-details-scrim" />
        <div className="dk-details-logo">
          <Logo game={game} className="dk-details-logo-img" titleClass="sr-only" />
        </div>
      </div>
      <div className="dk-details-body">
        <div>
          <h2 className="dk-details-title">{game.title}</h2>
          <p className="dk-muted">
            {SOURCE_LABEL[game.source]} · {STATUS_LABEL[status]}
          </p>
        </div>
        <div className="dk-details-actions" data-nav-group="details-actions">
          <PlayButton game={game} />
          <button
            className="dk-icon-btn"
            data-nav
            data-kind="details"
            aria-label="Open the game's page"
            title="Open the game's page"
            onClick={() => openGamePage(gameId)}
          >
            <ArrowUpRight aria-hidden />
          </button>
        </div>
        <div className="dk-details-stats">
          <Stat label="Play time">{history.totalMinutes ? fmtMinutes(history.totalMinutes) : "—"}</Stat>
          <Stat label="Last played">{lastDay !== undefined ? dayLabel(lastDay) : "—"}</Stat>
          <Stat label="Last 7 days">{history.last7Minutes ? fmtMinutes(history.last7Minutes) : "—"}</Stat>
          <Stat label="Sessions, 14 days">{history.sessionCount}</Stat>
        </div>
        {history.sessions.length > 0 && (
          <div>
            <h3 className="dk-h3">Recent sessions</h3>
            <ol className="dk-sessions dk-sessions-compact">
              {history.sessions.slice(0, 3).map((s) => (
                <li key={`${s.dayOffset}-${s.start}`}>
                  <span>
                    {dayLabel(s.dayOffset)} ·{" "}
                    <span className="num">
                      {s.start}–{sessionEnd(s)}
                    </span>
                  </span>
                  <span className="dk-sessions-dur num">{fmtMinutes(s.minutes)}</span>
                </li>
              ))}
            </ol>
          </div>
        )}
        {saves && (
          <p className="dk-details-saves">
            <SaveGlyph kind={saves.kind} />
            {saves.label}
          </p>
        )}
      </div>
    </aside>
  );
}
