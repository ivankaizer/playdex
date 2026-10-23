import {
  AppWindow,
  Archive,
  ExternalLink,
  FolderOpen,
  Gamepad2,
  Globe,
  MessagesSquare,
  Play,
  Plus,
  RefreshCw,
  Settings2,
  Trash2,
} from "lucide-react";
import { useEffect, useState } from "react";
import { launch, openApp, showToast } from "../app-state";
import { SaveGlyph } from "../components/saves";
import { Cover } from "../components/shared";
import { gameById, type Game } from "../data/games";
import { desktopState, rpc, type DesktopApp, type DesktopGame, type MessageView, type PlatformOption } from "../host/desktop";
import { focusEl, focusScreenDefault } from "../nav/focus";
import { useStore } from "../nav/store";
import "./desk.css";

/** How long Remove stays armed for its confirming second press. */
const ARM_MS = 4000;

function report(view: MessageView) {
  showToast(view.message);
}

function failed(error: unknown) {
  showToast(error instanceof Error ? error.message : String(error));
}

/**
 * The games added to Playdex (the WinForms Wrapper's list), as a table: play, edit their settings (inline, under the
 * row), add them to Steam with SteamGridDB art, back up their saves with Ludusavi, daily or now, remove them. Below
 * them, the apps Big Picture opens from quick settings. Desktop mode only.
 */
export function Manage() {
  const state = useStore(desktopState);
  const [editing, setEditing] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const loaded = state !== null;

  // This screen loads after the tab switch focused the page, so it lands on its own default once it has rendered.
  useEffect(() => {
    if (!loaded) return;
    const t = window.setTimeout(focusScreenDefault);
    return () => window.clearTimeout(t);
  }, [loaded]);

  async function run(key: string, work: () => Promise<unknown>) {
    setBusy(key);
    try {
      await work();
    } catch (error) {
      failed(error);
    } finally {
      setBusy(null);
    }
  }

  if (!state) {
    return (
      <div className="dk-page">
        <div className="desk-loading" />
      </div>
    );
  }

  return (
    <div className="dk-page dk-manage">
      <header className="dk-page-head">
        <div>
          <span className="dk-kicker">Added to Playdex</span>
          <h1 className="dk-h1">Your games</h1>
          <p className="dk-muted">
            Games Playdex starts and records from their .exe or .bat. Steam games installed on this PC are in the Library already.
          </p>
        </div>
        <div className="dk-head-actions" data-nav-group="manage-actions">
          <button
            className="dk-btn dk-btn-outline"
            data-nav
            data-kind="desk"
            disabled={busy === "sync" || !state.runAvailable}
            title="Regenerate each game's launcher (the exe its Steam shortcut starts)"
            onClick={() => run("sync", async () => report(await rpc<MessageView>("launchers.sync")))}
          >
            <RefreshCw aria-hidden /> Regenerate launchers
          </button>
          <button className="dk-btn dk-btn-outline" data-nav data-kind="desk" onClick={() => run("open", () => rpc("launchers.open"))}>
            <FolderOpen aria-hidden /> Launchers folder
          </button>
          <button
            className="dk-btn dk-btn-primary"
            data-nav
            data-nav-default
            data-kind="desk"
            disabled={busy === "add"}
            onClick={() => run("add", () => rpc("game.add"))}
          >
            <Plus aria-hidden /> Add a game
          </button>
        </div>
      </header>

      {state.games.length === 0 ? (
        <div className="dk-card dk-manage-empty">
          <Gamepad2 aria-hidden />
          <h2>No games added yet</h2>
          <p className="dk-muted">
            Add a game's .exe or .bat to track its sessions, start it from here or from Steam, and back up its saves. Steam games installed
            on this PC are in the Library already.
          </p>
        </div>
      ) : (
        <div className="dk-card dk-table-wrap">
          <table className="dk-table">
            <thead>
              <tr>
                <th scope="col">Game</th>
                <th scope="col">Recorded as</th>
                <th scope="col">Save backups</th>
                <th scope="col">Daily backup</th>
                <th scope="col">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {state.games.map((g) => (
                <GameRow
                  key={g.gameId}
                  game={g}
                  busy={busy}
                  editing={editing === g.gameId}
                  onEdit={() => setEditing(editing === g.gameId ? null : g.gameId)}
                  onClose={() => setEditing(null)}
                  run={run}
                />
              ))}
            </tbody>
          </table>
          <p className="dk-note dk-table-note">
            Steam must be closed to add a shortcut. With a SteamGridDB key in Settings it gets cover, hero, logo and icon art.
          </p>
        </div>
      )}
      <AppsSection apps={state.apps} suggested={state.suggestedApps} busy={busy} run={run} />
    </div>
  );
}

function GameRow({
  game,
  busy,
  editing,
  onEdit,
  onClose,
  run,
}: {
  game: DesktopGame;
  busy: string | null;
  editing: boolean;
  onEdit: () => void;
  onClose: () => void;
  run: (key: string, work: () => Promise<unknown>) => Promise<void>;
}) {
  const [armed, setArmed] = useState(false);
  const libraryId = game.libraryId;
  // Absent when the game's executable is missing (the library lists only games on this PC).
  const libraryGame: Game | undefined = gameById(libraryId);
  const settings = useStore(desktopState)?.settings;

  useEffect(() => {
    if (!armed) return;
    const t = window.setTimeout(() => setArmed(false), ARM_MS);
    return () => window.clearTimeout(t);
  }, [armed]);

  const remove = () => {
    if (!armed) {
      setArmed(true);
      return;
    }
    setArmed(false);
    void run(`remove:${game.gameId}`, () => rpc("game.remove", { gameId: game.gameId }));
  };

  return (
    <>
      <tr data-editing={editing ? "" : undefined} data-nav-group={`game-${game.gameId}`} data-nav-entry="first">
        <td>
          <div className="dk-cell-game">
            {libraryGame ? (
              <Cover game={libraryGame} kind="portrait" className="dk-cell-art" compact />
            ) : (
              <div className="cover dk-cell-art">
                <div className="cover-initial" aria-hidden>
                  {game.displayName.charAt(0)}
                </div>
              </div>
            )}
            <div className="dk-cell-text">
              <span className="dk-cell-title">{game.displayName}</span>
              <span className="dk-cell-path" data-missing={game.executableExists ? undefined : ""} title={game.executablePath}>
                {game.executableExists ? game.executablePath : `Missing: ${game.executablePath}`}
              </span>
            </div>
          </div>
        </td>
        <td>
          <div className="dk-cell-text">
            <span className="num">
              {game.gameProvider === "steam" ? "Steam" : game.gameProvider} · {game.apiGameId}
            </span>
            <span className="dk-muted">{game.platform ?? `Default (${settings?.platform ?? "pc"})`}</span>
          </div>
        </td>
        <td>
          <span className="dk-cell-backup" data-kind={game.backup.kind} title={game.backup.text}>
            <SaveGlyph kind={game.backup.kind} />
            {game.backup.text}
          </span>
        </td>
        <td>
          <button
            className="switch"
            role="switch"
            aria-checked={game.dailyBackup}
            aria-label={`Back up ${game.displayName}'s saves daily`}
            data-nav
            data-kind="desk"
            disabled={busy === `daily:${game.gameId}`}
            onClick={() => run(`daily:${game.gameId}`, () => rpc("game.dailyBackup", { gameId: game.gameId, enabled: !game.dailyBackup }))}
          >
            <i aria-hidden />
          </button>
        </td>
        <td>
          <div className="dk-cell-actions">
            <button
              className="dk-btn dk-btn-primary dk-btn-sm"
              data-nav
              data-kind="desk"
              disabled={!game.executableExists || !libraryGame}
              onClick={() => launch(libraryId)}
            >
              <Play aria-hidden /> Play
            </button>
            <button
              className="dk-btn dk-btn-outline dk-btn-sm"
              data-nav
              data-kind="desk"
              disabled={busy === `steam:${game.gameId}`}
              title="Add a non-Steam shortcut (Steam must be closed) and download SteamGridDB art"
              onClick={() => run(`steam:${game.gameId}`, async () => report(await rpc<MessageView>("game.steam", { gameId: game.gameId })))}
            >
              Add to Steam
            </button>
            <button
              className="dk-icon-btn"
              data-nav
              data-kind="desk"
              aria-label={`Back up ${game.displayName}'s saves now`}
              title="Back up saves now"
              disabled={busy === `backup:${game.gameId}` || game.backup.kind === "running"}
              onClick={() =>
                run(`backup:${game.gameId}`, async () => report(await rpc<MessageView>("game.backup", { gameId: game.gameId })))
              }
            >
              <Archive aria-hidden />
            </button>
            <button
              className="dk-icon-btn"
              data-nav
              data-kind="desk"
              aria-expanded={editing}
              aria-label={`${game.displayName} settings`}
              title="Settings"
              onClick={onEdit}
            >
              <Settings2 aria-hidden />
            </button>
            <button
              className="dk-icon-btn dk-danger"
              data-nav
              data-kind="desk"
              data-armed={armed ? "" : undefined}
              aria-label={armed ? `Remove ${game.displayName}? Press again to confirm` : `Remove ${game.displayName}`}
              title={armed ? "Press again to remove" : "Remove"}
              onClick={remove}
            >
              {armed ? <span className="dk-armed-text">Remove?</span> : <Trash2 aria-hidden />}
            </button>
          </div>
        </td>
      </tr>
      {editing && (
        <tr className="dk-table-editor">
          <td colSpan={5}>
            <GameEditor game={game} onClose={onClose} />
          </td>
        </tr>
      )}
    </>
  );
}

function GameEditor({ game, onClose }: { game: DesktopGame; onClose: () => void }) {
  const [name, setName] = useState(game.displayName);
  const [apiGameId, setApiGameId] = useState(game.apiGameId);
  const [launchOptions, setLaunchOptions] = useState(game.launchOptions);
  const [platform, setPlatform] = useState(game.platform ?? "");
  const [dailyBackup, setDailyBackup] = useState(game.dailyBackup);
  const [platforms, setPlatforms] = useState<PlatformOption[] | null>(null);
  const [saving, setSaving] = useState(false);
  const settings = useStore(desktopState)?.settings;

  useEffect(() => {
    rpc<{ platforms: PlatformOption[] }>("platforms")
      .then((r) => setPlatforms(r.platforms))
      .catch(() => setPlatforms([]));
    // Start typing in the first field.
    const t = window.setTimeout(() => focusEl(document.getElementById(`edit-name-${game.gameId}`)));
    return () => window.clearTimeout(t);
  }, [game.gameId]);

  async function save() {
    setSaving(true);
    try {
      await rpc("game.update", {
        gameId: game.gameId,
        displayName: name,
        apiGameId,
        launchOptions,
        platform: platform || null,
        dailyBackup,
      });
      showToast(`Saved ${name}.`);
      onClose();
    } catch (error) {
      failed(error);
    } finally {
      setSaving(false);
    }
  }

  const known = platforms ?? [];
  const options = platform && !known.some((p) => p.key === platform) ? [...known, { key: platform, name: platform }] : known;

  return (
    <form
      className="desk-form"
      data-nav-group={`edit-${game.gameId}`}
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
    >
      <label className="field">
        <span className="label">Name</span>
        <input
          id={`edit-name-${game.gameId}`}
          className="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          data-nav
          data-kind="field"
        />
      </label>
      <label className="field">
        <span className="label">Game ID</span>
        <input className="text num" value={apiGameId} onChange={(e) => setApiGameId(e.target.value)} data-nav data-kind="field" />
        <small>The Steam app id when the game is on Steam (SteamDB lists it); otherwise any stable id.</small>
      </label>
      <label className="field">
        <span className="label">Launch options</span>
        <input
          className="text"
          value={launchOptions}
          placeholder="Optional"
          onChange={(e) => setLaunchOptions(e.target.value)}
          data-nav
          data-kind="field"
        />
      </label>
      <label className="field">
        <span className="label">Platform</span>
        <select className="text" value={platform} onChange={(e) => setPlatform(e.target.value)} data-nav data-kind="field">
          <option value="">Default ({settings?.platform ?? "pc"})</option>
          {options.map((p) => (
            <option key={p.key} value={p.key}>
              {p.name}
            </option>
          ))}
        </select>
      </label>
      <label className="field field-check">
        <input type="checkbox" checked={dailyBackup} onChange={(e) => setDailyBackup(e.target.checked)} data-nav data-kind="field" />
        <span>Back up saves daily with Ludusavi</span>
      </label>
      <div className="desk-form-actions">
        <button type="submit" className="dk-btn dk-btn-primary" disabled={saving} data-nav data-kind="desk">
          Save
        </button>
        <button type="button" className="dk-btn dk-btn-outline" onClick={onClose} data-nav data-kind="desk">
          Cancel
        </button>
      </div>
    </form>
  );
}

function AppIcon({ kind }: { kind: string }) {
  if (kind === "browser") return <Globe aria-hidden />;
  if (kind.startsWith("discord")) return <MessagesSquare aria-hidden />;
  return <AppWindow aria-hidden />;
}

/**
 * The apps Big Picture opens from quick settings, for the Full Screen Experience, which has no taskbar or Start menu.
 * Only the apps the user adds: any .exe, or with one click Discord or the default browser when they are on this PC.
 */
function AppsSection({
  apps,
  suggested,
  busy,
  run,
}: {
  apps: DesktopApp[];
  suggested: DesktopApp[];
  busy: string | null;
  run: (key: string, work: () => Promise<unknown>) => Promise<void>;
}) {
  const [editing, setEditing] = useState<string | null>(null);
  return (
    <section className="dk-manage-apps" aria-labelledby="desk-apps-title">
      <header className="dk-page-head">
        <div>
          <h2 id="desk-apps-title" className="dk-h2">
            Apps in quick settings
          </h2>
          <p className="dk-muted">
            They open from quick settings (Menu) in Big Picture, since the Full Screen Experience has no taskbar. Apps aren't games: Playdex
            doesn't record them.
          </p>
        </div>
        <div className="dk-head-actions" data-nav-group="apps-actions">
          {suggested.map((a) => (
            <button
              key={a.kind}
              className="dk-btn dk-btn-outline"
              data-nav
              data-kind="desk"
              disabled={busy === `app-found:${a.kind}`}
              title={a.path}
              onClick={() => run(`app-found:${a.kind}`, () => rpc("app.addFound", { kind: a.kind }))}
            >
              <Plus aria-hidden /> {a.name}
            </button>
          ))}
          <button
            className="dk-btn dk-btn-outline"
            data-nav
            data-kind="desk"
            disabled={busy === "app-add"}
            onClick={() => run("app-add", () => rpc("app.add"))}
          >
            <Plus aria-hidden /> Add an app
          </button>
        </div>
      </header>
      {apps.length === 0 ? (
        <p className="dk-muted">No apps yet. Quick settings shows only Steam until you add one.</p>
      ) : (
        <div className="dk-card dk-table-wrap">
          <table className="dk-table">
            <thead>
              <tr>
                <th scope="col">App</th>
                <th scope="col">Starts with</th>
                <th scope="col">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {apps.map((a) => (
                <AppRow
                  key={a.id}
                  app={a}
                  editing={editing === a.id}
                  onEdit={() => setEditing(editing === a.id ? null : a.id)}
                  onClose={() => setEditing(null)}
                  run={run}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

function AppRow({
  app,
  editing,
  onEdit,
  onClose,
  run,
}: {
  app: DesktopApp;
  editing: boolean;
  onEdit: () => void;
  onClose: () => void;
  run: (key: string, work: () => Promise<unknown>) => Promise<void>;
}) {
  const [armed, setArmed] = useState(false);

  useEffect(() => {
    if (!armed) return;
    const t = window.setTimeout(() => setArmed(false), ARM_MS);
    return () => window.clearTimeout(t);
  }, [armed]);

  const remove = () => {
    if (!armed) {
      setArmed(true);
      return;
    }
    setArmed(false);
    void run(`app-remove:${app.id}`, () => rpc("app.remove", { id: app.id }));
  };

  const origin =
    app.kind === "browser"
      ? "Your default browser"
      : app.kind === "executable"
        ? "Added from its .exe"
        : "Started through Discord's updater";

  return (
    <>
      <tr data-editing={editing ? "" : undefined} data-nav-group={`app-${app.id}`} data-nav-entry="first">
        <td>
          <div className="dk-cell-game">
            <div className="dk-cell-art dk-app-icon">
              <AppIcon kind={app.kind} />
            </div>
            <div className="dk-cell-text">
              <span className="dk-cell-title">{app.name}</span>
              <span className="dk-muted">
                {origin}
                {app.launchOptions ? ` · ${app.launchOptions}` : ""}
              </span>
            </div>
          </div>
        </td>
        <td>
          <span className="dk-cell-path" data-missing={app.available ? undefined : ""} title={app.path}>
            {app.available ? app.path : app.path ? `Missing: ${app.path}` : "Not installed on this PC"}
          </span>
        </td>
        <td>
          <div className="dk-cell-actions">
            <button
              className="dk-btn dk-btn-outline dk-btn-sm"
              data-nav
              data-kind="desk"
              disabled={!app.available}
              onClick={() => openApp(app.id, app.name)}
            >
              <ExternalLink aria-hidden /> Open
            </button>
            <button
              className="dk-icon-btn"
              data-nav
              data-kind="desk"
              aria-expanded={editing}
              aria-label={`${app.name} settings`}
              title="Settings"
              onClick={onEdit}
            >
              <Settings2 aria-hidden />
            </button>
            <button
              className="dk-icon-btn dk-danger"
              data-nav
              data-kind="desk"
              data-armed={armed ? "" : undefined}
              aria-label={armed ? `Remove ${app.name}? Press again to confirm` : `Remove ${app.name}`}
              title={armed ? "Press again to remove" : "Remove"}
              onClick={remove}
            >
              {armed ? <span className="dk-armed-text">Remove?</span> : <Trash2 aria-hidden />}
            </button>
          </div>
        </td>
      </tr>
      {editing && (
        <tr className="dk-table-editor">
          <td colSpan={3}>
            <AppEditor app={app} onClose={onClose} />
          </td>
        </tr>
      )}
    </>
  );
}

function AppEditor({ app, onClose }: { app: DesktopApp; onClose: () => void }) {
  const [name, setName] = useState(app.name);
  const [launchOptions, setLaunchOptions] = useState(app.launchOptions);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    // Start typing in the first field.
    const t = window.setTimeout(() => focusEl(document.getElementById(`app-name-${app.id}`)));
    return () => window.clearTimeout(t);
  }, [app.id]);

  async function save() {
    setSaving(true);
    try {
      await rpc("app.update", { id: app.id, name, launchOptions });
      showToast(`Saved ${name}.`);
      onClose();
    } catch (error) {
      failed(error);
    } finally {
      setSaving(false);
    }
  }

  return (
    <form
      className="desk-form"
      data-nav-group={`app-edit-${app.id}`}
      onSubmit={(e) => {
        e.preventDefault();
        void save();
      }}
    >
      <label className="field">
        <span className="label">Name</span>
        <input
          id={`app-name-${app.id}`}
          className="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
          data-nav
          data-kind="field"
        />
      </label>
      <label className="field">
        <span className="label">Launch options</span>
        <input
          className="text"
          value={launchOptions}
          placeholder="Optional"
          onChange={(e) => setLaunchOptions(e.target.value)}
          data-nav
          data-kind="field"
        />
        <small>Used when the app starts. An app that is already running is switched to instead.</small>
      </label>
      <div className="desk-form-actions">
        <button type="submit" className="dk-btn dk-btn-primary" disabled={saving} data-nav data-kind="desk">
          Save
        </button>
        <button type="button" className="dk-btn dk-btn-outline" onClick={onClose} data-nav data-kind="desk">
          Cancel
        </button>
      </div>
    </form>
  );
}
