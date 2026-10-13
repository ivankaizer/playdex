import { Moon, SlidersHorizontal, Sun } from "lucide-react";
import { lazy, Suspense, useEffect, useRef, useState } from "react";
import {
  closeGame,
  launch,
  launching,
  leaveLaunch,
  nowPlaying,
  openGame,
  openGameId,
  openQuickSettings,
  quickSettingsOpen,
  resumeGame,
  session,
  takeReturnFocus,
  toast,
} from "./app-state";
import { QuickSettings } from "./components/QuickSettings";
import { Backdrop, Clock, formatClock, Glyph, Hint, Logo, useElapsed } from "./components/shared";
import { gameById, isInstalled, recentGames } from "./data/games";
import { DossierHome } from "./homes/DossierHome";
import { RibbonHome } from "./homes/RibbonHome";
import { TimelineHome } from "./homes/TimelineHome";
import { focusEl, focusScreenDefault, forgetGroups } from "./nav/focus";
import { onAction } from "./nav/input";
import { focusedGame, focusedKind, useStore } from "./nav/store";
import { inHost, onHostMessage, signalAfterFrame } from "./host/bridge";
import { desktopState, isDesktop, loadDesktopState } from "./host/desktop";
import { GamePage } from "./screens/GamePage";
import { Library } from "./screens/Library";
import { PlaydexScreen } from "./screens/Playdex";

// Loaded when first opened: Big Picture never downloads Manage (desktop only), nor Settings until its tab is opened.
const Manage = lazy(() => import("./screens/Manage").then((m) => ({ default: m.Manage })));
const Settings = lazy(() => import("./screens/Settings").then((m) => ({ default: m.Settings })));

let readySent = false;

/*
 * Home is the Ribbon. The earlier prototypes stay reachable for development only, with ?layout=now or
 * ?layout=timeline in the page URL; nothing in the UI switches layouts and no choice is saved.
 */
const LAYOUTS = ["ribbon", "now", "timeline"] as const;
type LayoutId = (typeof LAYOUTS)[number];
const layout: LayoutId = (() => {
  const v = new URLSearchParams(location.search).get("layout");
  return (LAYOUTS as readonly string[]).includes(v ?? "") ? (v as LayoutId) : "ribbon";
})();

/*
 * Desktop mode adds the games added to Playdex (the WinForms Wrapper's list). Settings are in both modes: all of them in
 * desktop mode, the ones a controller can change in Big Picture.
 */
const ALL_TABS = ["Home", "Library", "Playdex", "Manage", "Settings"] as const;
type Tab = (typeof ALL_TABS)[number];
const TABS: readonly Tab[] = isDesktop ? ALL_TABS : ["Home", "Library", "Playdex", "Settings"];

/** The desktop app reloads the page after games change; the open tab survives that. */
function initialTab(): Tab {
  if (!isDesktop) return "Home";
  try {
    const saved = sessionStorage.getItem("pdx.tab");
    return TABS.includes(saved as Tab) ? (saved as Tab) : "Home";
  } catch {
    return "Home";
  }
}

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
  const [theme, setTheme] = useState<"dark" | "light">(() => read("theme", ["dark", "light"], "dark"));
  const [tab, setTab] = useState<Tab>(initialTab);
  const playing = useStore(launching);
  const gameOpen = useStore(openGameId);
  const settingsOpen = useStore(quickSettingsOpen);
  const toastMsg = useStore(toast);

  useEffect(() => {
    document.documentElement.dataset.theme = theme;
    persist("theme", theme);
  }, [theme]);

  useEffect(() => {
    if (!isDesktop) return;
    try {
      sessionStorage.setItem("pdx.tab", tab);
    } catch {
      /* storage unavailable */
    }
  }, [tab]);

  // Big Picture loads it off the start-up path: it only marks the Settings tab when an update is ready.
  useEffect(() => {
    const t = window.setTimeout(() => void loadDesktopState().catch(() => undefined), isDesktop ? 0 : 3000);
    return () => window.clearTimeout(t);
  }, []);

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
      else focusScreenDefault();
      if (!readySent) {
        readySent = true;
        signalAfterFrame({ type: "ready" });
      }
    });
    return () => window.clearTimeout(t);
  }, [tab, playing, gameOpen]);

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
        if (a === "menu") {
          openQuickSettings();
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
        if (a === "y" && game && !openGameId.get() && ["game", "session", "continue", "playing", "cover", "cover-off"].includes(kind)) {
          openGame(game);
          return true;
        }
        if (a === "x" && game && kind === "cover" && isInstalled(gameById(game))) {
          launch(game);
          return true;
        }
      }),
    [],
  );

  return (
    <>
      <div className="screen" inert={playing || settingsOpen ? true : undefined}>
        <header className="topbar">
          <span className="brand">
            <span className="brand-dot" />
            PLAYDEX
          </span>
          <nav className="tabs" aria-label="Sections">
            {!isDesktop && <Glyph name="LB" />}
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
                  {t === "Settings" && <UpdateDot />}
                </button>
              ))}
            </div>
            {!isDesktop && <Glyph name="RB" />}
          </nav>
          <span className="topbar-spacer" />
          <NowPlaying />
          <div className="proto" data-nav-group="proto">
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
          <div className="status" data-nav-group="status">
            <button
              className="icon-btn"
              data-nav
              data-kind="quick-settings"
              aria-label="Quick settings"
              aria-haspopup="dialog"
              onClick={openQuickSettings}
            >
              <SlidersHorizontal />
            </button>
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
        ) : tab === "Manage" ? (
          <Suspense fallback={<main />}>
            <Manage />
          </Suspense>
        ) : tab === "Settings" ? (
          <Suspense fallback={<main />}>
            <Settings />
          </Suspense>
        ) : tab === "Home" ? (
          recentGames.length === 0 ? (
            <EmptyHome />
          ) : layout === "ribbon" ? (
            <RibbonHome />
          ) : layout === "now" ? (
            <DossierHome />
          ) : (
            <TimelineHome />
          )
        ) : (
          <PlaydexScreen />
        )}

        <HintBar onGamePage={!!gameOpen} />
      </div>

      {settingsOpen && !playing && <QuickSettings />}
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
  backup: "Back up",
  "daily-backup": "Daily backup",
  details: "Open",
  "now-playing": "Resume",
  playing: "Resume",
  steam: "Open",
};

/** On the Settings tab while an update waits for a restart. Its own component, so state pushes don't re-render App. */
function UpdateDot() {
  const ready = useStore(desktopState)?.update.kind === "ready";
  return ready ? <span className="tab-dot" role="img" aria-label="Update ready" /> : null;
}

/** The running game in the top bar, however it was started; A switches to it. */
function NowPlaying() {
  const playing = useStore(nowPlaying);
  if (!playing) return null;
  return <NowPlayingPill gameId={playing.gameId} since={playing.since} />;
}

function NowPlayingPill({ gameId, since }: { gameId: string; since: number | null }) {
  const game = gameById(gameId);
  const elapsed = useElapsed(since);
  return (
    <div data-nav-group="now-playing">
      <button
        className="now-playing"
        data-nav
        data-kind="now-playing"
        aria-label={`Now playing ${game.title}. Resume`}
        onClick={resumeGame}
      >
        <span className="live-dot" aria-hidden />
        <span className="now-playing-label">Playing</span>
        <span className="now-playing-title">{game.title}</span>
        {elapsed !== null && <span className="now-playing-clock num">{formatClock(elapsed)}</span>}
      </button>
    </div>
  );
}

function HintBar({ onGamePage }: { onGamePage: boolean }) {
  const kind = useStore(focusedKind);
  return (
    <footer className="hints">
      <Hint glyph="A">{PRIMARY[kind] ?? "Select"}</Hint>
      {kind === "cover" && <Hint glyph="X">Play</Hint>}
      {!onGamePage && ["game", "session", "continue", "playing", "cover", "cover-off"].includes(kind) && <Hint glyph="Y">Details</Hint>}
      {onGamePage && <Hint glyph="B">Back</Hint>}
      <Hint glyph="LB">Sections</Hint>
      <Hint glyph="MENU">Quick settings</Hint>
      <span className="sync">
        <span className="sync-dot" />
        Synced with Playdex
      </span>
    </footer>
  );
}

function EmptyHome() {
  return (
    <main className="placeholder">
      <div className="card">
        <span className="kicker">No games yet</span>
        <h2>Install a game on Steam or add one in Playdex</h2>
        <p>
          The launcher lists installed Steam games and games added through the Playdex Wrapper. New games show up the
          next time the launcher starts.
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
  const runningRef = useRef(running);
  runningRef.current = running;

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
        if (a === "confirm" && runningRef.current) {
          resumeGame();
          return true;
        }
        return true;
      }),
    [],
  );

  const playedFor = inHost ? (hostSession?.seconds ?? 0) + elapsed : Math.max(0, elapsed - 3);
  const clock = formatClock(playedFor);

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
          {running && <Hint glyph="A">Resume game</Hint>}
          <Hint glyph="B">Back to home</Hint>
          <span>The game window takes over here; the Xbox button brings this screen back.</span>
        </footer>
      </div>
    </div>
  );
}
