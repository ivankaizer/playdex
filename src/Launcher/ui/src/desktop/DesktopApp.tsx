import { Gamepad2, Moon, SlidersHorizontal, Sun, UserRound, UserRoundCheck, UserRoundX, X } from "lucide-react";
import { lazy, Suspense, useEffect } from "react";
import {
  closeGame,
  launchGuard,
  launching,
  leaveLaunch,
  nowPlaying,
  openGameId,
  openQuickSettings,
  quickSettingsOpen,
  resumeGame,
  session,
  showToast,
  toast,
} from "../app-state";
import { LaunchGuard } from "../components/LaunchGuard";
import { QuickSettings } from "../components/QuickSettings";
import { formatClock, useElapsed } from "../components/shared";
import { gameById, markPlayed } from "../data/games";
import { playdexConnected, playdexSummary } from "../data/playdex";
import { inHost, onHostMessage, signalAfterFrame } from "../host/bridge";
import { desktopState, loadDesktopState, switchMode } from "../host/desktop";
import { focusScreenDefault, forgetGroups } from "../nav/focus";
import { onAction } from "../nav/input";
import { inputMode, useStore } from "../nav/store";
import { PlaydexScreen } from "../screens/Playdex";
import { scheme, setScheme } from "../theme";
import { GameView } from "./GameView";
import { Home } from "./Home";
import { LibraryGrid } from "./LibraryGrid";
import { Sidebar } from "./Sidebar";
import { DESK_TABS, deskTab, goTo, libraryView, openGamePage, selectedGame } from "./state";
import "./desktop.css";

const Manage = lazy(() => import("../screens/Manage").then((m) => ({ default: m.Manage })));
const Settings = lazy(() => import("../screens/Settings").then((m) => ({ default: m.Settings })));

let readySent = false;

/** In a plain browser a launch is simulated: the game "runs" after this long. */
const DEMO_START_MS = 2500;

/**
 * Desktop mode: a window laid out like a desktop game library (Steam, Playnite) rather than the 10-foot screen.
 * Sections along the top; Home and the Library's list keep the game list down the left; the Library also has a cover
 * grid with filters. A launch shows in the top bar instead of taking over the window. Same tokens as Big Picture, so
 * the palette and light or dark apply here too.
 */
export function DesktopApp() {
  const tab = useStore(deskTab);
  const view = useStore(libraryView);
  const settingsOpen = useStore(quickSettingsOpen);
  const guard = useStore(launchGuard);
  const toastMsg = useStore(toast);

  useEffect(() => void loadDesktopState().catch(() => undefined), []);

  useLaunchInWindow();

  // Tab moves focus as the arrows do: show the ring (the arrows and a controller switch to "pad" in nav/input.ts).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Tab" || inputMode.get() === "pad") return;
      inputMode.set("pad");
      document.documentElement.dataset.input = "pad";
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  // A new screen lands on its default item for the keyboard and a controller (not for the mouse); the first one tells
  // the host it is up.
  useEffect(() => {
    forgetGroups();
    const t = window.setTimeout(() => {
      if (inputMode.get() === "pad") focusScreenDefault();
      if (!readySent) {
        readySent = true;
        signalAfterFrame({ type: "ready" });
      }
    });
    return () => window.clearTimeout(t);
  }, [tab, view]);

  // The host asks for home on every relaunch (a second instance, the tray, a protocol link).
  useEffect(
    () =>
      onHostMessage((m) => {
        if (m.type !== "home") return;
        goTo("Home");
        signalAfterFrame({ type: "home-ready", reason: "activate" });
      }),
    [],
  );

  // Screens shared with Big Picture open a game's page through openGame (the Playdex tab): show it in the Library.
  useEffect(() => {
    const off = openGameId.subscribe(() => {
      const id = openGameId.get();
      if (!id) return;
      closeGame();
      openGamePage(id);
    });
    return () => void off();
  }, []);

  useEffect(
    () =>
      onAction((a) => {
        if (a === "guide") {
          goTo("Home");
          return true;
        }
        if (a === "menu") {
          openQuickSettings();
          return true;
        }
        if (a === "lb" || a === "rb") {
          const i = DESK_TABS.indexOf(deskTab.get());
          goTo(DESK_TABS[(i + (a === "rb" ? 1 : DESK_TABS.length - 1)) % DESK_TABS.length]);
          return true;
        }
      }),
    [],
  );

  const withList = tab === "Home" || (tab === "Library" && view === "list");

  return (
    <>
      <div className="dk" inert={settingsOpen || guard ? true : undefined}>
        <TopBar tab={tab} />
        <div className="dk-body">
          {withList && <Sidebar />}
          {tab === "Library" && view === "grid" ? (
            <LibraryGrid />
          ) : (
            <main className="dk-main" data-scroll="y" data-tab={tab}>
              <Screen tab={tab} />
            </main>
          )}
        </div>
      </div>
      {settingsOpen && <QuickSettings />}
      <LaunchGuard />
      {toastMsg && (
        <div className="toast" role="status">
          {toastMsg}
        </div>
      )}
    </>
  );
}

function Screen({ tab }: { tab: (typeof DESK_TABS)[number] }) {
  const selected = useStore(selectedGame);
  switch (tab) {
    case "Home":
      return <Home />;
    case "Library":
      return selected && gameById(selected) ? (
        <GameView key={selected} gameId={selected} />
      ) : (
        <div className="dk-page dk-empty">
          <p className="dk-muted">Pick a game on the left.</p>
        </div>
      );
    case "Playdex":
      return (
        <div className="dk-page dk-legacy">
          <PlaydexScreen />
        </div>
      );
    case "Manage":
      return (
        <Suspense fallback={null}>
          <Manage />
        </Suspense>
      );
    case "Settings":
      return (
        <div className="dk-page dk-legacy">
          <Suspense fallback={null}>
            <Settings />
          </Suspense>
        </div>
      );
  }
}

/**
 * A launch in a window: the top bar says it is starting (with Cancel) until the game runs, then the game shows as
 * playing there. In a plain browser the game "starts" after a moment.
 */
function useLaunchInWindow() {
  const gameId = useStore(launching);
  const state = useStore(session)?.state;
  useEffect(() => {
    if (!gameId) return;
    if (inHost) {
      // Running: tracking goes on without the launch; the host reports the game as now playing.
      if (state === "running") leaveLaunch();
      return;
    }
    const t = window.setTimeout(() => {
      leaveLaunch();
      markPlayed(gameId);
      nowPlaying.set({ gameId, since: Date.now() });
    }, DEMO_START_MS);
    return () => window.clearTimeout(t);
  }, [gameId, state]);
}

function TopBar({ tab }: { tab: (typeof DESK_TABS)[number] }) {
  const theme = useStore(scheme);
  return (
    <header className="dk-top">
      <span className="brand">
        <span className="brand-dot" />
        PLAYDEX
      </span>
      <nav className="dk-tabs" aria-label="Sections" data-nav-group="tabs">
        {DESK_TABS.map((t) => (
          <button key={t} aria-current={tab === t ? "page" : undefined} data-nav data-kind="tab" onClick={() => goTo(t)}>
            {t}
            {t === "Settings" && <UpdateDot />}
          </button>
        ))}
      </nav>
      <span className="dk-spacer" />
      <LaunchStatus />
      <SyncStatus />
      <div className="dk-top-tools" data-nav-group="top-tools">
        <button
          className="dk-icon-btn"
          data-nav
          data-kind="theme"
          aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
          title={theme === "dark" ? "Light theme" : "Dark theme"}
          onClick={() => setScheme(theme === "dark" ? "light" : "dark")}
        >
          {theme === "dark" ? <Sun aria-hidden /> : <Moon aria-hidden />}
        </button>
        <button className="dk-icon-btn" data-nav data-kind="quick-settings" aria-label="Quick settings" aria-haspopup="dialog" title="Quick settings" onClick={openQuickSettings}>
          <SlidersHorizontal aria-hidden />
        </button>
        <button
          className="dk-btn dk-btn-outline dk-btn-sm"
          data-nav
          data-kind="desk"
          onClick={() => switchMode("bigPicture").catch((e: unknown) => showToast(e instanceof Error ? e.message : String(e)))}
        >
          <Gamepad2 aria-hidden /> Big Picture
        </button>
        <Account />
      </div>
    </header>
  );
}

/** Starting a game (with Cancel), or the game running now, however it was started (switches to it). */
function LaunchStatus() {
  const starting = useStore(launching);
  const state = useStore(session)?.state;
  const playing = useStore(nowPlaying);
  if (starting) {
    const title = gameById(starting).title;
    return (
      <div className="dk-live" role="status" data-nav-group="launch">
        <span className="live-dot" aria-hidden />
        <span className="dk-live-text">{state === "steam" ? "Starting Steam" : `Starting ${title}`}</span>
        <button className="dk-live-x" data-nav data-kind="desk" aria-label={`Cancel starting ${title}`} title="Cancel" onClick={leaveLaunch}>
          <X aria-hidden />
        </button>
      </div>
    );
  }
  if (!playing) return null;
  return <Playing gameId={playing.gameId} since={playing.since} />;
}

function Playing({ gameId, since }: { gameId: string; since: number | null }) {
  const game = gameById(gameId);
  const elapsed = useElapsed(since);
  return (
    <div data-nav-group="now-playing">
      <button className="dk-live" data-nav data-kind="now-playing" aria-label={`Now playing ${game.title}. Switch to the game`} onClick={() => resumeGame(gameId)}>
        <span className="live-dot" aria-hidden />
        <span className="dk-live-label">Playing</span>
        <span className="dk-live-text">{game.title}</span>
        {elapsed !== null && <span className="dk-live-clock num">{formatClock(elapsed)}</span>}
      </button>
    </div>
  );
}

/** Whether sessions reach Playdex: synced once a summary has arrived, a way to connect without a key. */
function SyncStatus() {
  const summary = useStore(playdexSummary);
  if (!playdexConnected) {
    return (
      <button className="dk-sync dk-sync-off" onClick={() => goTo("Settings")}>
        Connect Playdex
      </button>
    );
  }
  return (
    <span className="dk-sync" title={summary ? `Updated ${new Date(summary.generatedAt).toLocaleTimeString()}` : undefined}>
      <span className="sync-dot" aria-hidden />
      {summary ? "Synced" : "Syncing…"}
    </span>
  );
}

/** The Playdex account: connected (an API key is saved) or not. Opens Settings. */
function Account() {
  const keySet = useStore(desktopState)?.settings.apiKeySet;
  const [Icon, label] =
    keySet === undefined
      ? [UserRound, "Playdex account"]
      : keySet
        ? [UserRoundCheck, "Connected to Playdex"]
        : [UserRoundX, "Not connected to Playdex: add an API key in Settings"];
  return (
    <button
      className="avatar"
      data-state={keySet === undefined ? undefined : keySet ? "on" : "off"}
      data-nav
      data-kind="account"
      aria-label={label}
      title={label}
      onClick={() => goTo("Settings")}
    >
      <Icon aria-hidden />
    </button>
  );
}

/** On the Settings tab while an update waits for a restart. */
function UpdateDot() {
  const ready = useStore(desktopState)?.update.kind === "ready";
  return ready ? <span className="tab-dot" role="img" aria-label="Update ready" /> : null;
}
