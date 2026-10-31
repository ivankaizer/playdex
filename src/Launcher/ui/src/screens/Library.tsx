import { Download, ExternalLink, MonitorSmartphone } from "lucide-react";
import { memo, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { openGame, openSteam } from "../app-state";
import { SaveGlyph, useSaves } from "../components/saves";
import { Cover } from "../components/shared";
import {
  fmtHours,
  games,
  isInstalled,
  isOnPc,
  launcherOf,
  recentGames,
  SOURCE_LABEL,
  useRecentGames,
  type Game,
  type GameStats,
} from "../data/games";
import { playdexGame, playdexSummary } from "../data/playdex";
import { useStore } from "../nav/store";
import "./library.css";

/*
 * By launcher (launcherOf: a launcher's game stays under it once added to Playdex), and local games: added from their
 * .exe through the desktop app or Wrapper. Steam and Local are always there; another launcher's tab only once it has
 * games on this PC.
 */
const SCOPES = [
  { id: "all", label: "All", test: () => true, empty: "Nothing here yet.", always: true },
  { id: "steam", label: "Steam", test: (g: Game) => launcherOf(g) === "steam", empty: "No Steam games installed.", always: true },
  { id: "xbox", label: "Xbox", test: (g: Game) => launcherOf(g) === "xbox", empty: "No Xbox app games installed.", always: false },
  { id: "epic", label: "Epic", test: (g: Game) => launcherOf(g) === "epic", empty: "No Epic games installed.", always: false },
  { id: "gog", label: "GOG", test: (g: Game) => launcherOf(g) === "gog", empty: "No GOG games installed.", always: false },
  {
    id: "local",
    label: "Local",
    test: (g: Game) => launcherOf(g) === "playdex",
    empty: "Games you add in the Playdex desktop app show up here.",
    always: true,
  },
] as const;

/** The tabs with their counts; the library is read once, so this is too. */
const TABS = SCOPES.map((s) => ({ ...s, count: games.filter(s.test).length })).filter((s) => s.always || s.count > 0);

/** Position in the recent games, rebuilt when a game is played (the order is live). */
let rank: { of: GameStats[]; at: Map<string, number> } = { of: [], at: new Map() };
function lastPlayedRank(id: string): number {
  if (rank.of !== recentGames) rank = { of: recentGames, at: new Map(recentGames.map((r, i) => [r.game.id, i])) };
  return rank.at.get(id) ?? 1e9;
}

const SORTS = [
  {
    id: "recent",
    label: "Recent",
    cmp: (a: Game, b: Game) => lastPlayedRank(a.id) - lastPlayedRank(b.id) || a.title.localeCompare(b.title),
  },
  { id: "az", label: "A–Z", cmp: (a: Game, b: Game) => a.title.localeCompare(b.title) },
  { id: "most", label: "Most played", cmp: (a: Game, b: Game) => b.totalMinutes - a.totalMinutes },
] as const;

type ScopeId = (typeof SCOPES)[number]["id"];
type SortId = (typeof SORTS)[number]["id"];

const OVERSCAN_ROWS = 2;

/*
 * Grid geometry comes from CSS (library.css: --lib-col-w, --lib-gap-x, --lib-row-h), so a theme can resize the covers
 * and the virtualizer follows. They are registered lengths, so their computed values arrive in px.
 */
function geometry(el: HTMLElement) {
  const s = getComputedStyle(el);
  const px = (name: string) => parseFloat(s.getPropertyValue(name)) || 0;
  return { w: el.clientWidth, h: el.clientHeight, col: px("--lib-col-w"), gap: px("--lib-gap-x"), row: px("--lib-row-h") };
}

export function Library() {
  const [scope, setScope] = useState<ScopeId>("all");
  const [sort, setSort] = useState<SortId>("recent");
  const recent = useRecentGames();

  // Filtered and sorted once per choice (and per game played, for Recent), never per render or per focus move.
  const list = useMemo(() => {
    const s = SCOPES.find((x) => x.id === scope)!;
    const o = SORTS.find((x) => x.id === sort)!;
    return games.filter(s.test).sort(o.cmp);
  }, [scope, sort, sort === "recent" ? recent : null]);

  return (
    <main className="library">
      <header className="lib-head">
        <div className="seg" role="group" aria-label="Source" data-nav-group="scope">
          {TABS.map((s) => (
            <button key={s.id} className="seg-btn" aria-pressed={scope === s.id} data-nav data-kind="filter" onClick={() => setScope(s.id)}>
              {s.label}
              <span className="lib-count num">{s.count}</span>
            </button>
          ))}
        </div>
        <span className="topbar-spacer" />
        <span className="label">Sort</span>
        <div className="seg" role="group" aria-label="Sort" data-nav-group="sort">
          {SORTS.map((s) => (
            <button key={s.id} className="seg-btn" aria-pressed={sort === s.id} data-nav data-kind="filter" onClick={() => setSort(s.id)}>
              {s.label}
            </button>
          ))}
        </div>
        <div data-nav-group="lib-steam">
          <button className="btn btn-outline lib-steam" data-nav data-kind="steam" onClick={openSteam}>
            <ExternalLink aria-hidden /> Open Steam
          </button>
        </div>
      </header>
      <CoverGrid key={`${scope}-${sort}`} list={list} empty={SCOPES.find((x) => x.id === scope)!.empty} />
    </main>
  );
}

/** Row-virtualized grid: only the rows in view (plus overscan) are mounted, whatever the library size. */
function CoverGrid({ list, empty }: { list: Game[]; empty: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const [box, setBox] = useState({ w: 0, h: 0, col: 0, gap: 0, row: 0 });
  const [range, setRange] = useState({ first: 0, last: 3 });

  useLayoutEffect(() => {
    const el = ref.current!;
    const measure = () => setBox(geometry(el));
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
  }, []);

  const cols = box.col > 0 ? Math.max(1, Math.floor((box.w + box.gap) / (box.col + box.gap))) : 1;
  const rows = Math.ceil(list.length / cols);
  const rowPx = box.row;

  useEffect(() => {
    const el = ref.current!;
    const update = () => {
      if (!rowPx) return;
      const first = Math.max(0, Math.floor(el.scrollTop / rowPx) - OVERSCAN_ROWS);
      const last = Math.min(rows - 1, Math.ceil((el.scrollTop + el.clientHeight) / rowPx) + OVERSCAN_ROWS);
      setRange((r) => (r.first === first && r.last === last ? r : { first, last }));
    };
    update();
    el.addEventListener("scroll", update, { passive: true });
    return () => el.removeEventListener("scroll", update);
  }, [rowPx, rows, box.h]);

  const rowEls = [];
  for (let r = range.first; r <= range.last && r < rows; r++) {
    rowEls.push(
      <div
        key={r}
        className="lib-row"
        style={{ transform: `translateY(${r * rowPx}px)`, gridTemplateColumns: `repeat(${cols}, var(--lib-col-w))` }}
      >
        {list.slice(r * cols, r * cols + cols).map((g, i) => (
          <CoverItem key={g.id} game={g} isDefault={r === 0 && i === 0} />
        ))}
      </div>,
    );
  }

  return (
    <div className="lib-scroll" ref={ref} data-scroll="y" data-scroll-center data-nav-group="grid">
      {list.length === 0 ? (
        <p className="lib-empty">{empty}</p>
      ) : (
        <div className="lib-canvas" style={{ height: `${rows * rowPx}px` }}>
          {rowEls}
        </div>
      )}
    </div>
  );
}

const CoverItem = memo(function CoverItem({ game, isDefault }: { game: Game; isDefault: boolean }) {
  const onPc = isOnPc(game);
  const installed = isInstalled(game);
  // Hours played as Playdex has them; "—" for a game with no Playdex sessions.
  const played = playdexGame(useStore(playdexSummary), game);
  const saves = useSaves(game.id)?.status;
  return (
    <button
      className="lcover"
      data-nav
      data-nav-default={isDefault ? "" : undefined}
      data-game={game.id}
      data-kind={installed ? "cover" : "cover-off"}
      data-state={installed ? undefined : onPc ? "missing" : "elsewhere"}
      onClick={() => openGame(game.id)}
    >
      <span className="lcover-art">
        <Cover game={game} kind="portrait" />
        {!installed && (
          <span className="lcover-badge">
            {onPc ? <Download /> : <MonitorSmartphone />}
            {onPc ? "Not installed" : SOURCE_LABEL[launcherOf(game)]}
          </span>
        )}
        {saves && (
          <span className="lcover-save" title={saves.label}>
            <SaveGlyph kind={saves.kind} />
          </span>
        )}
      </span>
      <span className="lcover-title">{game.title}</span>
      <span className="lcover-sub num">
        {fmtHours(played ? Math.round(played.totalSeconds / 60) : 0)} · {SOURCE_LABEL[launcherOf(game)]}
      </span>
    </button>
  );
});
