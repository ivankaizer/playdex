import { Download, MonitorPlay, Power, Save } from "lucide-react";
import { useEffect, useState } from "react";
import { focusDefault } from "../nav/focus";
import { showToast } from "../app-state";
import { desktopState, rpc, type DesktopSettings, type MessageView, type PlatformOption } from "../host/desktop";
import { useStore } from "../nav/store";
import "./desk.css";

function failed(error: unknown) {
  showToast(error instanceof Error ? error.message : String(error));
}

/**
 * The desktop app's settings (the WinForms Wrapper's Settings dialog, plus the app itself): the Playdex API, SteamGridDB
 * and Ludusavi, starting with Windows, updates and the Full Screen Experience launcher. Keys never come back to the
 * page: a saved key shows as saved, and typing replaces it.
 */
export function Settings() {
  const state = useStore(desktopState);
  return state ? <SettingsForm key={state.version} saved={state.settings} /> : <main className="desk"><div className="desk-loading" /></main>;
}

function SettingsForm({ saved }: { saved: DesktopSettings }) {
  const state = useStore(desktopState)!;
  const [baseUrl, setBaseUrl] = useState(saved.baseUrl);
  const [apiKey, setApiKey] = useState("");
  const [platform, setPlatform] = useState(saved.platform);
  const [steamGridDbKey, setSteamGridDbKey] = useState("");
  const [ludusaviPath, setLudusaviPath] = useState(saved.ludusaviPath);
  const [startWithWindows, setStartWithWindows] = useState(saved.startWithWindows);
  const [platforms, setPlatforms] = useState<PlatformOption[]>([]);
  const [platformError, setPlatformError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  // This screen loads after the tab switch focused the page, so it lands on its own default once it has rendered.
  useEffect(() => {
    const t = window.setTimeout(focusDefault);
    return () => window.clearTimeout(t);
  }, []);

  useEffect(() => {
    rpc<{ platforms: PlatformOption[]; error?: string }>("platforms")
      .then((r) => {
        setPlatforms(r.platforms);
        setPlatformError(r.error ?? null);
      })
      .catch((e) => setPlatformError(String(e)));
  }, []);

  async function save() {
    setSaving(true);
    try {
      await rpc("settings.save", {
        baseUrl,
        apiKey: apiKey || null,
        platform,
        steamGridDbKey: steamGridDbKey || null,
        ludusaviPath,
        startWithWindows,
      });
      setApiKey("");
      setSteamGridDbKey("");
      showToast("Settings saved.");
    } catch (error) {
      failed(error);
    } finally {
      setSaving(false);
    }
  }

  async function browse() {
    try {
      const r = await rpc<{ path?: string }>("settings.browseLudusavi");
      if (r.path) setLudusaviPath(r.path);
    } catch (error) {
      failed(error);
    }
  }

  const platformOptions = platforms.some((p) => p.key === platform) ? platforms : [{ key: platform, name: platform }, ...platforms];

  return (
    <main className="desk">
      <header className="desk-head">
        <div>
          <span className="kicker">Playdex {state.version}</span>
          <h1 className="desk-title">Settings</h1>
        </div>
        <div className="desk-actions" data-nav-group="settings-actions">
          <button className="btn btn-primary" data-nav data-nav-default data-kind="desk" disabled={saving} onClick={() => void save()}>
            <Save aria-hidden /> Save
          </button>
        </div>
      </header>

      <div className="desk-sections" data-scroll="y">
        <section className="card desk-section" data-nav-group="settings-api" data-nav-entry="first">
          <h2 className="desk-section-title">Your Playdex account</h2>
          <p className="desk-note">Sessions are recorded with this API key. Create one in Playdex under Integrations.</p>
          <label className="field">
            <span className="label">API URL</span>
            <input className="text" value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} data-nav data-kind="field" />
          </label>
          <label className="field">
            <span className="label">API key</span>
            <input
              className="text"
              type="password"
              autoComplete="off"
              value={apiKey}
              placeholder={saved.apiKeySet ? "Saved — type a new key to replace it" : "Not set"}
              onChange={(e) => setApiKey(e.target.value)}
              data-nav
              data-kind="field"
            />
          </label>
          <label className="field">
            <span className="label">Default platform</span>
            <select className="text" value={platform} onChange={(e) => setPlatform(e.target.value)} data-nav data-kind="field">
              {platformOptions.map((p) => (
                <option key={p.key} value={p.key}>
                  {p.name}
                </option>
              ))}
            </select>
            {platformError && <small>{platformError}</small>}
          </label>
        </section>

        <section className="card desk-section" data-nav-group="settings-art" data-nav-entry="first">
          <h2 className="desk-section-title">Steam artwork</h2>
          <p className="desk-note">With a SteamGridDB key, “Add to Steam” also downloads cover, hero, logo and icon art.</p>
          <label className="field">
            <span className="label">SteamGridDB API key</span>
            <input
              className="text"
              type="password"
              autoComplete="off"
              value={steamGridDbKey}
              placeholder={saved.steamGridDbKeySet ? "Saved — type a new key to replace it" : "Optional"}
              onChange={(e) => setSteamGridDbKey(e.target.value)}
              data-nav
              data-kind="field"
            />
          </label>
        </section>

        <section className="card desk-section" data-nav-group="settings-backup" data-nav-entry="first">
          <h2 className="desk-section-title">Save backups</h2>
          <p className="desk-note">Ludusavi backs up save files, daily for the games where you turn it on.</p>
          <div className="field">
            <span className="label">Ludusavi</span>
            <div className="field-row">
              <input
                className="text"
                value={ludusaviPath}
                placeholder="Path to ludusavi.exe"
                onChange={(e) => setLudusaviPath(e.target.value)}
                data-nav
                data-kind="field"
              />
              <button className="btn btn-outline" data-nav data-kind="desk" onClick={() => void browse()}>
                Browse…
              </button>
            </div>
          </div>
        </section>

        <section className="card desk-section" data-nav-group="settings-app" data-nav-entry="first">
          <h2 className="desk-section-title">This app</h2>
          <label className="field field-check">
            <input type="checkbox" checked={startWithWindows} onChange={(e) => setStartWithWindows(e.target.checked)} data-nav data-kind="field" />
            <span>Start with Windows, in the tray (backups and game launchers stay up to date)</span>
          </label>
          <div className="desk-line">
            <Download aria-hidden />
            <span className="desk-line-text">{state.update.message}</span>
            {state.update.kind === "ready" ? (
              <button className="btn btn-primary" data-nav data-kind="desk" onClick={() => rpc<MessageView>("update.apply").catch(failed)}>
                Restart to update
              </button>
            ) : (
              <button
                className="btn btn-outline"
                data-nav
                data-kind="desk"
                disabled={state.update.kind === "unavailable" || state.update.kind === "checking" || state.update.kind === "downloading"}
                onClick={() => rpc<MessageView>("update.check").catch(failed)}
              >
                Check for updates
              </button>
            )}
          </div>
          <div className="desk-line">
            <MonitorPlay aria-hidden />
            <span className="desk-line-text">{state.fse.message}</span>
            {state.fse.state === "available" || state.fse.state === "failed" ? (
              <button
                className="btn btn-outline"
                data-nav
                data-kind="desk"
                onClick={() => rpc<MessageView>("fse.install").then((r) => showToast(r.message), failed)}
              >
                Install
              </button>
            ) : null}
          </div>
          <div className="desk-line">
            <Power aria-hidden />
            <span className="desk-line-text">Playdex keeps running in the tray when its window is closed.</span>
            <button className="btn btn-outline" data-nav data-kind="desk" onClick={() => rpc("app.quit").catch(failed)}>
              Quit Playdex
            </button>
          </div>
        </section>
      </div>
    </main>
  );
}
