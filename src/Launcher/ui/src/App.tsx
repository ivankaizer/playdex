import { Moon, Sun, Wifi } from "lucide-react";
import { useEffect, useState } from "react";
import {
  closeGame,
  launch,
  launching,
  leaveLaunch,
  openGame,
  openGameId,
  session,
  takeReturnFocus,
  toast,
} from "./app-state";
import { Backdrop, Clock, Glyph, Hint, Logo } from "./components/shared";
import { gameById, isInstalled } from "./data/games";
import { DossierHome } from "./homes/DossierHome";
import { RibbonHome } from "./homes/RibbonHome";
import { TimelineHome } from "./homes/TimelineHome";
import { focusDefault, focusEl, forgetGroups } from "./nav/focus";
import { onAction } from "./nav/input";
import { focusedGame, focusedKind, useStore } from "./nav/store";
import { inHost, onHostMessage, signalAfterFrame } from "./host/bridge";
import { GamePage } from "./screens/GamePage";
import { Library } from "./screens/Library";

let readySent = false;

const LAYOUTS = [
  { id: "ribbon", label: "Ribbon" },
  { id: "now", label: "Now card" },
  { id: "timeline", label: "Timeline" },
] as const;
type LayoutId = (typeof LAYOUTS)[number]["id"];

const TABS = ["Home", "Library", "Playdex"] as const;
type Tab = (typeof TABS)[number];

function read<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  const fromUrl = new URLSearchParams(location.search).get(key);
  let stored: string | null = null;
  try {
    stored = localStorage.getItem(`pdx.${key}`);
  } catch {
    /* storage unavailable */
  }
  const v = fromUrl ?? stored;
  return (allowed as readonly string[]).includes(v ?? "") ? (v as T) : fallback;
}

function persist(key: string, value: string) {
  try {
    localStorage.setItem(`pdx.${key}`, value);
  } catch {
    /* storage unavailable */
  }
}

export function App() {
  const [layout, setLayout] = useState<LayoutId>(() => read("layout", LAYOUTS.map((l) => l.id), "ribbon"));
  const [theme, setTheme] = useState<"dark" | "light">(() => read("theme", ["dark", "light"], "dark"));
  const [tab, setTab] = useState<Tab>("Home");
  const playing = useStore(launching);
  const gameOpen = useStore(openGameId);
  const toastMsg = useStore(toast);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    persist("theme", theme);
  }, [theme]);

  useEffect(() => {
    persist("layout", layout);
    const url = new URL(location.href);
    url.searchParams.set("layout", layout);
    history.replaceState(null, "", url);
  }, [layout]);

  // New screen: forget per-group focus memory and land on the screen's default item. Closing a game page
  // returns focus to the tile that opened it.
  useEffect(() => {
    if (playing) return;
    const back = gameOpen ? null : takeReturnFocus();
    forgetGroups();
    // A timer, not rAF: rAF pauses while the window is hidden, and focus must be ready when it reappears.
    const t = window.setTimeout(() => {
      const from = back ? document.querySelector<HTMLElement>(`[data-nav][data-game="${back}"]`) : null;
      if (from) focusEl(from);
      else focusDefault();
      if (!readySent) {
        readySent = true;
        signalAfterFrame({ type: "ready" });
      }
    });
    return () => window.clearTimeout(t);
  }, [layout, tab, playing, gameOpen]);

  // The host asks for home on every relaunch (FSE, Game Bar "Home", a second instance) and on the Guide button.
  useEffect(
    () =>
      onHostMessage((m) => {
        if (m.type !== "home") return;
        closeGame();
        setTab("Home");
        signalAfterFrame({ type: "home-ready", reason: "activate" });
      }),
    [],
  );

  useEffect(
    () =>
      onAction((a) => {
        if (launching.get()) return false;
        const kind = focusedKind.get();
        const game = focusedGame.get();
        if (a === "guide") {
          closeGame();
          setTab("Home");
          return true;
        }
        if (a === "back" && openGameId.get()) {
          closeGame();
          return true;
        }
        if (a === "lb" || a === "rb") {
          closeGame();
          setTab((t) => TABS[(TABS.indexOf(t) + (a === "rb" ? 1 : TABS.length - 1)) % TABS.length]);
          return true;
        }
        if (a === "y" && game && !openGameId.get() && ["game", "session", "continue", "cover", "cover-off"].includes(kind)) {
          openGame(game);
          return true;
        }
        if (a === "x" && game && kind === "cover" && isInstalled(gameById(game))) {
          launch(game);
          return true;
        }
        if (a === "view" && !openGameId.get()) {
          setLayout((l) => LAYOUTS[(LAYOUTS.findIndex((x) => x.id === l) + 1) % LAYOUTS.length].id);
          return true;
        }
      }),
    [],
  );

  return (
    <>
      <div className="screen" inert={playing ? true : undefined}>
        <header className="topbar">
          <span className="brand">
            <span className="brand-dot" />
            PLAYDEX
          </span>
          <nav className="tabs" aria-label="Sections">
            <Glyph name="LB" />
            <div className="seg" role="tablist" data-nav-group="tabs">
              {TABS.map((t) => (
                <button
                  key={t}
                  role="tab"
                  className="seg-btn"
                  aria-selected={tab === t}
                  data-nav
                  data-kind="tab"
                  onClick={() => {
                    closeGame();
                    setTab(t);
                  }}
                >
                  {t}
                </button>
              ))}
            </div>
            <Glyph name="RB" />
          </nav>
          <span className="topbar-spacer" />
          <div className="proto" data-nav-group="proto">
            <span className="label">Layout</span>
            <div className="seg" role="group" aria-label="Home layout">
              {LAYOUTS.map((l) => (
                <button
                  key={l.id}
                  className="seg-btn"
                  aria-pressed={layout === l.id}
                  data-nav
                  data-kind="layout"
                  onClick={() => setLayout(l.id)}
                >
                  {l.label}
                </button>
              ))}
            </div>
            <button
              className="icon-btn"
              data-nav
              data-kind="theme"
              aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
              onClick={() => setTheme((t) => (t === "dark" ? "light" : "dark"))}
            >
              {theme === "dark" ? <Sun /> : <Moon />}
            </button>
          </div>
          <div className="status">
            <Wifi aria-label="Online" />
            <Clock />
            <span className="avatar" aria-label="Signed in to Playdex">
              I
            </span>
          </div>
        </header>

        {gameOpen ? (
          <GamePage key={gameOpen} gameId={gameOpen} />
        ) : tab === "Library" ? (
          <Library />
        ) : tab === "Home" ? (
          layout === "ribbon" ? (
            <RibbonHome />
          ) : layout === "now" ? (
            <DossierHome />
          ) : (
            <TimelineHome />
          )
        ) : (
          <Placeholder tab={tab} />
        )}

        <HintBar onGamePage={!!gameOpen} onHome={tab === "Home"} />
      </div>

      {playing && <LaunchOverlay gameId={playing} />}
      {toastMsg && (
        <div className="toast" role="status">
          {toastMsg}
        </div>
      )}
    </>
  );
}

const PRIMARY: Record<string, string> = {
  game: "Play",
  session: "Play",
  continue: "Play",
  cover: "Open",
  "cover-off": "Open",
  install: "Install",
  favorite: "Favorite",
  details: "Open",
};

function HintBar({ onGamePage, onHome }: { onGamePage: boolean; onHome: boolean }) {
  const kind = useStore(focusedKind);
  return (
    <footer className="hints">
      <Hint glyph="A">{PRIMARY[kind] ?? "Select"}</Hint>
      {kind === "cover" && <Hint glyph="X">Play</Hint>}
      {!onGamePage && ["game", "session", "continue", "cover", "cover-off"].includes(kind) && <Hint glyph="Y">Details</Hint>}
      {onGamePage && <Hint glyph="B">Back</Hint>}
      <Hint glyph="LB">Sections</Hint>
      {onHome && !onGamePage && <Hint glyph="VIEW">Home layout</Hint>}
      <span className="sync">
        <span className="sync-dot" />
        Synced with Playdex
      </span>
    </footer>
  );
}

function Placeholder({ tab }: { tab: Tab }) {
  return (
    <main className="placeholder">
      <div className="card">
        <span className="kicker">{tab}</span>
        <h2>Your Playdex stats are designed next</h2>
        <p>
          Recaps, streaks and per-platform totals from your Playdex account will live here. Switch sections with{" "}
          <Glyph name="LB" /> <Glyph name="RB" />.
        </p>
      </div>
    </main>
  );
}

function LaunchOverlay({ gameId }: { gameId: string }) {
  const game = gameById(gameId);
  const hostSession = useStore(session);
  const [elapsed, setElapsed] = useState(0);

  // In the host, "running" and the session seconds come from the host; it reports exit too. The 1 s ticker only
  // advances the clock between host updates. In a plain browser the launch is simulated: running after 3 s.
  const running = inHost ? hostSession?.state === "running" : elapsed >= 3;

  useEffect(() => {
    if (inHost && !running) return;
    setElapsed(0);
    const t = window.setInterval(() => setElapsed((e) => e + 1), 1000);
    return () => window.clearInterval(t);
  }, [running, hostSession?.seconds]);

  useEffect(
    () =>
      onAction((a) => {
        if (a === "back") {
          leaveLaunch();
          return true;
        }
        return true;
      }),
    [],
  );

  const playedFor = inHost ? (hostSession?.seconds ?? 0) + elapsed : Math.max(0, elapsed - 3);
  const clock = `${Math.floor(playedFor / 60)}:${String(playedFor % 60).padStart(2, "0")}`;

  return (
    <div className="launch" role="dialog" aria-modal="true" aria-label={`Launching ${game.title}`}>
      <Backdrop gameId={gameId} />
      <div className="launch-scrim" />
      <div className="launch-body">
        <Logo game={game} className="launch-logo" titleClass="launch-title" />
        {running ? (
          <div className="launch-state">
            <span className="rec">
              <span className="live-dot" />
              Recording to Playdex
            </span>
            <span className="num">Playing · {clock}</span>
          </div>
        ) : (
          <div className="launch-state">
            Starting {game.title}
            <span className="progress">
              <i />
            </span>
          </div>
        )}
        <footer className="hints">
          <Hint glyph="B">Back to home</Hint>
          <span>The game window takes over here; the Xbox button brings this screen back.</span>
        </footer>
      </div>
    </div>
  );
}
