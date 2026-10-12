import { Archive, FolderOpen, Gamepad2, Play, Plus, RefreshCw, Settings2, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { launch, showToast } from "../app-state";
import { Cover } from "../components/shared";
import { gameById, type Game } from "../data/games";
import {
  desktopState,
  rpc,
  type DesktopGame,
  type MessageView,
  type PlatformOption,
} from "../host/desktop";
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
 * The games added to Playdex (the WinForms Wrapper's list): play, edit their settings, add them to Steam with
 * SteamGridDB art, back up their saves with Ludusavi, remove them. Each game's settings open inline under it.
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
      <main className="desk">
        <div className="desk-loading" />
      </main>
    );
  }

  return (
    <main className="desk">
      <header className="desk-head">
        <div>
          <span className="kicker">Added to Playdex</span>
          <h1 className="desk-title">Your games</h1>
        </div>
        <div className="desk-actions" data-nav-group="manage-actions">
          <button className="btn btn-primary" data-nav data-nav-default data-kind="desk" disabled={busy === "add"} onClick={() => run("add", () => rpc("game.add"))}>
            <Plus aria-hidden /> Add a game
          </button>
          <button
            className="btn btn-outline"
            data-nav
            data-kind="desk"
            disabled={busy === "sync" || !state.runAvailable}
            title="Regenerate each game's launcher (the exe its Steam shortcut starts)"
            onClick={() => run("sync", async () => report(await rpc<MessageView>("launchers.sync")))}
          >
            <RefreshCw aria-hidden /> Regenerate launchers
          </button>
          <button className="btn btn-outline" data-nav data-kind="desk" onClick={() => run("open", () => rpc("launchers.open"))}>
            <FolderOpen aria-hidden /> Launchers folder
          </button>
        </div>
      </header>

      {state.games.length === 0 ? (
        <div className="card desk-empty">
          <Gamepad2 aria-hidden />
          <h2>No games added yet</h2>
          <p>
            Add a game's .exe to track its sessions, start it from here or from Steam, and back up its saves. Steam games
            installed on this PC are in the Library already.
          </p>
        </div>
      ) : (
        <ul className="desk-list" data-scroll="y">
          {state.games.map((g) => (
            <li key={g.gameId}>
              <GameRow
                game={g}
                busy={busy}
                editing={editing === g.gameId}
                onEdit={() => setEditing(editing === g.gameId ? null : g.gameId)}
                onClose={() => setEditing(null)}
                run={run}
              />
            </li>
          ))}
        </ul>
      )}
    </main>
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
  const libraryId = `playdex-${game.gameId}`;
  // Absent when the game's executable is missing (the library lists only games on this PC).
  const libraryGame: Game | undefined = gameById(libraryId);

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
    <article className="card desk-row" data-editing={editing ? "" : undefined}>
      {/* Entering a game lands on Play, not on whichever action happens to be nearest (Remove, from the header). */}
      <div className="desk-row-main" data-nav-group={`game-${game.gameId}`} data-nav-entry="first">
        {libraryGame ? (
          <Cover game={libraryGame} kind="portrait" className="desk-thumb" compact />
        ) : (
          <div className="cover desk-thumb">
            <div className="cover-initial" aria-hidden>
              {game.displayName.charAt(0)}
            </div>
          </div>
        )}
        <div className="desk-row-text">
          <h2 className="desk-row-title">{game.displayName}</h2>
          <span className="desk-row-sub">
            {game.gameProvider === "steam" ? "Steam" : game.gameProvider} · {game.apiGameId}
            {game.platform ? ` · ${game.platform}` : ""}
          </span>
          <span className="desk-row-path" title={game.executablePath}>
            {game.executableExists ? game.executablePath : `Missing: ${game.executablePath}`}
          </span>
        </div>
        <span className="desk-chip" data-kind={game.backup.kind} title={game.backup.text}>
          <Archive aria-hidden />
          {game.backup.text}
        </span>
        <div className="desk-row-actions">
          <button
            className="btn btn-primary"
            data-nav
            data-kind="desk"
            disabled={!game.executableExists || !libraryGame}
            onClick={() => launch(libraryId)}
          >
            <Play aria-hidden /> Play
          </button>
          <button className="btn btn-outline" data-nav data-kind="desk" aria-expanded={editing} onClick={onEdit}>
            <Settings2 aria-hidden /> Settings
          </button>
          <button
            className="btn btn-outline"
            data-nav
            data-kind="desk"
            disabled={busy === `steam:${game.gameId}`}
            title="Add a non-Steam shortcut (Steam must be closed) and download SteamGridDB art"
            onClick={() => run(`steam:${game.gameId}`, async () => report(await rpc<MessageView>("game.steam", { gameId: game.gameId })))}
          >
            Add to Steam
          </button>
          <button
            className="btn btn-outline"
            data-nav
            data-kind="desk"
            disabled={busy === `backup:${game.gameId}` || game.backup.kind === "running"}
            onClick={() => run(`backup:${game.gameId}`, async () => report(await rpc<MessageView>("game.backup", { gameId: game.gameId })))}
          >
            Back up saves
          </button>
          <button
            className="btn btn-outline desk-danger"
            data-nav
            data-kind="desk"
            data-armed={armed ? "" : undefined}
            aria-label={armed ? `Remove ${game.displayName}? Press again to confirm` : `Remove ${game.displayName}`}
            onClick={remove}
          >
            <Trash2 aria-hidden /> {armed ? "Remove?" : "Remove"}
          </button>
        </div>
      </div>
      {editing && <GameEditor game={game} onClose={onClose} />}
    </article>
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
      await rpc("game.update", { gameId: game.gameId, displayName: name, apiGameId, launchOptions, platform: platform || null, dailyBackup });
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
        <input id={`edit-name-${game.gameId}`} className="text" value={name} onChange={(e) => setName(e.target.value)} data-nav data-kind="field" />
      </label>
      <label className="field">
        <span className="label">Game ID</span>
        <input className="text num" value={apiGameId} onChange={(e) => setApiGameId(e.target.value)} data-nav data-kind="field" />
        <small>The Steam app id when the game is on Steam (SteamDB lists it); otherwise any stable id.</small>
      </label>
      <label className="field">
        <span className="label">Launch options</span>
        <input className="text" value={launchOptions} placeholder="Optional" onChange={(e) => setLaunchOptions(e.target.value)} data-nav data-kind="field" />
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
        <button type="submit" className="btn btn-primary" disabled={saving} data-nav data-kind="desk">
          Save
        </button>
        <button type="button" className="btn btn-outline" onClick={onClose} data-nav data-kind="desk">
          Cancel
        </button>
      </div>
    </form>
  );
}
