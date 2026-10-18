import {
  AppWindow,
  Archive,
  Check,
  Download,
  Gamepad2,
  ImageIcon,
  LayoutGrid,
  MonitorPlay,
  Palette,
  PanelsTopLeft,
  Power,
  RotateCw,
  UserRound,
} from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { focusScreenDefault } from "../nav/focus";
import { showToast } from "../app-state";
import {
  desktopState,
  isDesktop,
  loadDesktopState,
  rpc,
  setStartWithWindows,
  switchMode,
  type DesktopSettings,
  type DesktopState,
  type FseView,
  type MessageView,
  type PlatformOption,
  type UpdateView,
} from "../host/desktop";
import { HOME_ART, homeArt, nextHomeArt, setHomeArt } from "../homes/homeArt";
import { useStore } from "../nav/store";
import { nextTheme, palette, PALETTES, setTheme, skin, SKINS } from "../theme";
import "./desk.css";
import "./settings.css";

function failed(error: unknown) {
  showToast(error instanceof Error ? error.message : String(error));
}

/**
 * The app's settings. Desktop mode has all of them (the WinForms Wrapper's Settings dialog, plus the app itself): the
 * Playdex API, SteamGridDB and Ludusavi, starting with Windows, the mode Playdex opens in, updates and the Full Screen
 * Experience entry. Big Picture has the ones a controller can change (updates, mode, starting with Windows) and shows
 * the rest as they are; keys and paths are typed in desktop mode. Keys never come back to the page: a saved key shows
 * as saved, and typing replaces it.
 */
export function Settings() {
  const state = useStore(desktopState);

  useEffect(() => {
    if (!desktopState.get()) void loadDesktopState().catch(failed);
  }, []);

  if (!state) {
    return (
      <main className="desk">
        <div className="desk-loading" />
      </main>
    );
  }
  return isDesktop ? <DesktopSettingsScreen state={state} /> : <BigPictureSettings state={state} />;
}

/** Lands on the screen's default item once it has rendered (it loads after the tab switch focused the page). */
function useLandOnDefault() {
  useEffect(() => {
    const t = window.setTimeout(focusScreenDefault);
    return () => window.clearTimeout(t);
  }, []);
}

const updateBusy = (update: UpdateView) => ["unavailable", "checking", "downloading", "applying"].includes(update.kind);

/** Restart to update when one is ready, else check for one. */
function UpdateButton({ update, className = "", isDefault = false }: { update: UpdateView; className?: string; isDefault?: boolean }) {
  const ready = update.kind === "ready";
  return (
    <button
      className={`btn ${ready ? "btn-primary" : "btn-outline"} ${className}`}
      data-nav
      data-nav-default={isDefault ? "" : undefined}
      data-kind="setting"
      disabled={!ready && updateBusy(update)}
      onClick={() => rpc<MessageView>(ready ? "update.apply" : "update.check").catch(failed)}
    >
      {ready ? <RotateCw aria-hidden /> : <Download aria-hidden />}
      {ready ? "Restart to update" : update.kind === "checking" ? "Checking…" : "Check for updates"}
    </button>
  );
}

/** An on/off switch that applies at once. */
function Switch({ on, label, onChange }: { on: boolean; label: string; onChange: (on: boolean) => void }) {
  return (
    <button className="switch" role="switch" aria-checked={on} aria-label={label} data-nav data-kind="setting" onClick={() => onChange(!on)}>
      <i aria-hidden />
    </button>
  );
}

function useStartWithWindows(saved: boolean) {
  // Shown switched at once; the host's reply (the new state) confirms it, or a failure puts it back.
  const [pending, setPending] = useState<boolean | null>(null);
  const change = (on: boolean) => {
    setPending(on);
    setStartWithWindows(on)
      .catch(failed)
      .finally(() => setPending(null));
  };
  return [pending ?? saved, change] as const;
}

/* ---------- Desktop mode ---------- */

interface Form {
  baseUrl: string;
  apiKey: string;
  platform: string;
  steamGridDbKey: string;
  ludusaviPath: string;
}

const formOf = (s: DesktopSettings): Form => ({
  baseUrl: s.baseUrl,
  apiKey: "",
  platform: s.platform,
  steamGridDbKey: "",
  ludusaviPath: s.ludusaviPath,
});

/** A group of settings: what it is about on the left, its rows on the right. */
function Section({ id, icon, title, note, children }: { id: string; icon: ReactNode; title: string; note: string; children: ReactNode }) {
  return (
    <section className="set-section" aria-labelledby={`set-${id}`}>
      <div className="set-about">
        <span className="set-icon">{icon}</span>
        <h2 id={`set-${id}`} className="set-section-title">
          {title}
        </h2>
        <p className="set-note">{note}</p>
      </div>
      <div className="card set-rows" data-nav-group={`settings-${id}`}>
        {children}
      </div>
    </section>
  );
}

/** One setting: its name and an explanation, and the control that changes it. */
function Row({ label, hint, htmlFor, children }: { label: string; hint?: ReactNode; htmlFor?: string; children: ReactNode }) {
  return (
    <div className="set-row">
      <div className="set-row-text">
        {htmlFor ? (
          <label className="set-row-label" htmlFor={htmlFor}>
            {label}
          </label>
        ) : (
          <span className="set-row-label">{label}</span>
        )}
        {hint && <span className="set-row-hint">{hint}</span>}
      </div>
      <div className="set-row-control">{children}</div>
    </div>
  );
}

function DesktopSettingsScreen({ state }: { state: DesktopState }) {
  const saved = state.settings;
  const [form, setForm] = useState(() => formOf(saved));
  const [platforms, setPlatforms] = useState<PlatformOption[]>([]);
  const [platformError, setPlatformError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [startWithWindows, changeStartWithWindows] = useStartWithWindows(saved.startWithWindows);
  const art = useStore(homeArt);
  const skinId = useStore(skin);
  const paletteId = useStore(palette);

  useLandOnDefault();

  useEffect(() => {
    rpc<{ platforms: PlatformOption[]; error?: string }>("platforms")
      .then((r) => {
        setPlatforms(r.platforms);
        setPlatformError(r.error ?? null);
      })
      .catch((e) => setPlatformError(String(e)));
  }, []);

  const set = (field: keyof Form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [field]: e.target.value }));

  const dirty =
    form.baseUrl !== saved.baseUrl ||
    form.apiKey !== "" ||
    form.platform !== saved.platform ||
    form.steamGridDbKey !== "" ||
    form.ludusaviPath !== saved.ludusaviPath;

  async function save() {
    if (saving) return;
    setSaving(true);
    try {
      const next = await rpc<DesktopState>("settings.save", {
        baseUrl: form.baseUrl,
        apiKey: form.apiKey || null,
        platform: form.platform,
        steamGridDbKey: form.steamGridDbKey || null,
        ludusaviPath: form.ludusaviPath,
      });
      // The host normalizes what it saves (a trailing slash, quotes around a path): show what it kept.
      setForm(formOf(next.settings));
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
      if (r.path) setForm((f) => ({ ...f, ludusaviPath: r.path! }));
    } catch (error) {
      failed(error);
    }
  }

  /** Enter in a field saves, as in a dialog. */
  const onEnter = (e: React.KeyboardEvent) => {
    if (e.key === "Enter" && dirty) void save();
  };

  const platformOptions = platforms.some((p) => p.key === form.platform) ? platforms : [{ key: form.platform, name: form.platform }, ...platforms];
  const update = state.update;
  const fseAction = state.fse.state === "available" || state.fse.state === "failed";

  return (
    <main className="desk set">
      <header className="desk-head">
        <div>
          <span className="kicker">Playdex {state.version}</span>
          <h1 className="desk-title">Settings</h1>
        </div>
        <div className="set-update" data-kind={update.kind} data-nav-group="settings-update">
          <span className="set-update-text" role="status">
            {update.message}
          </span>
          <UpdateButton update={update} />
        </div>
      </header>

      <div className="set-scroll" data-scroll="y">
        <div className="set-column">
          <Section
            id="account"
            icon={<UserRound />}
            title="Playdex account"
            note="Sessions are recorded with this API key. Create one in Playdex under Integrations."
          >
            <Row label="API URL" htmlFor="set-base-url">
              <input id="set-base-url" className="text" value={form.baseUrl} onChange={set("baseUrl")} onKeyDown={onEnter} data-nav data-nav-default data-kind="field" />
            </Row>
            <Row
              label="API key"
              htmlFor="set-api-key"
              hint={saved.apiKeySet ? <SavedMark>Saved</SavedMark> : "Needed to record sessions."}
            >
              <input
                id="set-api-key"
                className="text"
                type="password"
                autoComplete="off"
                value={form.apiKey}
                placeholder={saved.apiKeySet ? "Type a new key to replace it" : "Paste your API key"}
                onChange={set("apiKey")}
                onKeyDown={onEnter}
                data-nav
                data-kind="field"
              />
            </Row>
            <Row label="Default platform" htmlFor="set-platform" hint={platformError ?? "The platform sessions are recorded on, unless a game sets its own."}>
              <select id="set-platform" className="text" value={form.platform} onChange={set("platform")} data-nav data-kind="field">
                {platformOptions.map((p) => (
                  <option key={p.key} value={p.key}>
                    {p.name}
                  </option>
                ))}
              </select>
            </Row>
          </Section>

          <Section
            id="art"
            icon={<ImageIcon />}
            title="Steam artwork"
            note="With a SteamGridDB key, a game’s Artwork button picks its poster, background and logo, and “Add to Steam” downloads its art."
          >
            <Row
              label="SteamGridDB API key"
              htmlFor="set-sgdb-key"
              hint={saved.steamGridDbKeySet ? <SavedMark>Saved</SavedMark> : "Optional."}
            >
              <input
                id="set-sgdb-key"
                className="text"
                type="password"
                autoComplete="off"
                value={form.steamGridDbKey}
                placeholder={saved.steamGridDbKeySet ? "Type a new key to replace it" : "Paste your SteamGridDB key"}
                onChange={set("steamGridDbKey")}
                onKeyDown={onEnter}
                data-nav
                data-kind="field"
              />
            </Row>
          </Section>

          <Section
            id="backup"
            icon={<Archive />}
            title="Save backups"
            note="Ludusavi backs up save files, daily for the games where you turn it on in Manage."
          >
            <Row label="Ludusavi" htmlFor="set-ludusavi" hint="ludusavi.exe on this PC.">
              <div className="field-row">
                <input
                  id="set-ludusavi"
                  className="text"
                  value={form.ludusaviPath}
                  placeholder="Path to ludusavi.exe"
                  onChange={set("ludusaviPath")}
                  onKeyDown={onEnter}
                  data-nav
                  data-kind="field"
                />
                <button className="btn btn-outline" data-nav data-kind="setting" onClick={() => void browse()}>
                  Browse…
                </button>
              </div>
            </Row>
          </Section>

          <Section id="look" icon={<LayoutGrid />} title="Appearance" note="How Playdex looks. Applies at once, on this screen.">
            <Row label="Home game art" hint={HOME_ART.find((a) => a.id === art)!.note}>
              <div className="seg" role="radiogroup" aria-label="Home game art">
                {HOME_ART.map((a) => (
                  <button
                    key={a.id}
                    className="seg-btn"
                    role="radio"
                    aria-checked={art === a.id}
                    data-nav
                    data-kind="setting"
                    onClick={() => setHomeArt(a.id)}
                  >
                    {a.label}
                  </button>
                ))}
              </div>
            </Row>
            <Row label="Layout" hint={SKINS.find((o) => o.id === skinId)?.note ?? FROM_URL}>
              <div className="seg" role="radiogroup" aria-label="Layout">
                {SKINS.map((o) => (
                  <button
                    key={o.id}
                    className="seg-btn"
                    role="radio"
                    aria-checked={skinId === o.id}
                    data-nav
                    data-kind="setting"
                    onClick={() => setTheme("skin", o.id)}
                  >
                    {o.label}
                  </button>
                ))}
              </div>
            </Row>
            <Row label="Colours" hint={PALETTES.find((o) => o.id === paletteId)?.note ?? FROM_URL}>
              <div className="swatches" role="radiogroup" aria-label="Colours">
                {PALETTES.map((o) => (
                  <button
                    key={o.id}
                    className="swatch-btn"
                    role="radio"
                    aria-checked={paletteId === o.id}
                    data-nav
                    data-kind="setting"
                    onClick={() => setTheme("palette", o.id)}
                  >
                    <Swatch colors={o.swatch} />
                    {o.label}
                  </button>
                ))}
              </div>
            </Row>
          </Section>

          <Section id="app" icon={<AppWindow />} title="This app" note="How Playdex opens, and what it does in the background.">
            <Row label="Big Picture" hint="Full screen and controller first, for the TV or a handheld. Playdex then opens that way; switch back from Quick settings.">
              <button className="btn btn-outline" data-nav data-kind="setting" onClick={() => switchMode("bigPicture").catch(failed)}>
                <Gamepad2 aria-hidden /> Switch to Big Picture
              </button>
            </Row>
            <Row label="Start with Windows" hint="In the tray, so backups and game launchers stay up to date.">
              <Switch on={startWithWindows} label="Start with Windows" onChange={changeStartWithWindows} />
            </Row>
            <Row label="Full Screen Experience" hint={state.fse.message}>
              {fseAction ? (
                <button
                  className="btn btn-outline"
                  data-nav
                  data-kind="setting"
                  onClick={() => rpc<MessageView>("fse.install").then((r) => showToast(r.message), failed)}
                >
                  <MonitorPlay aria-hidden /> Install
                </button>
              ) : (
                <FseStatus fse={state.fse} />
              )}
            </Row>
            <Row label="Quit Playdex" hint="Closing the window keeps Playdex running in the tray.">
              <button className="btn btn-outline" data-nav data-kind="setting" onClick={() => rpc("app.quit").catch(failed)}>
                <Power aria-hidden /> Quit
              </button>
            </Row>
          </Section>
        </div>
      </div>

      {dirty && (
        <div className="set-savebar" role="region" aria-label="Unsaved changes" data-nav-group="settings-save">
          <span className="set-savebar-text">You have unsaved changes.</span>
          <button className="btn btn-outline" data-nav data-kind="setting" disabled={saving} onClick={() => setForm(formOf(saved))}>
            Discard
          </button>
          <button className="btn btn-primary" data-nav data-kind="setting" disabled={saving} onClick={() => void save()}>
            {saving ? "Saving…" : "Save changes"}
          </button>
        </div>
      )}
    </main>
  );
}

/** Shown for a theme picked in the page URL that the lists here don't have. */
const FROM_URL = "A theme from the page URL.";

/** A palette at a glance: its page colour and two accents, overlapping. */
function Swatch({ colors }: { colors: readonly string[] }) {
  return (
    <span className="swatch" aria-hidden>
      {colors.map((c) => (
        <i key={c} style={{ background: c }} />
      ))}
    </span>
  );
}

function SavedMark({ children }: { children: ReactNode }) {
  return (
    <span className="set-saved">
      <Check aria-hidden /> {children}
    </span>
  );
}

function FseStatus({ fse }: { fse: FseView }) {
  if (fse.state === "installed") return <SavedMark>Installed</SavedMark>;
  if (fse.state === "installing") return <span className="set-row-hint">Installing…</span>;
  return null;
}

/* ---------- Big Picture ---------- */

/** What a typed setting is now, for a row that can't change it with a controller. */
function StatusRow({ icon, label, value, on }: { icon: ReactNode; label: string; value: string; on: boolean }) {
  return (
    <div className="bp-row bp-row-static">
      {icon}
      <span className="bp-row-text">
        <span className="bp-row-name">{label}</span>
        <small>{value}</small>
      </span>
      <span className="bp-dot" data-on={on ? "" : undefined} aria-hidden />
    </div>
  );
}

function hostOf(url: string) {
  try {
    return new URL(url).host;
  } catch {
    return url;
  }
}

function BigPictureSettings({ state }: { state: DesktopState }) {
  const { settings, update, fse } = state;
  const [startWithWindows, changeStartWithWindows] = useStartWithWindows(settings.startWithWindows);
  const ready = update.kind === "ready";
  const artId = useStore(homeArt);
  const art = HOME_ART.find((a) => a.id === artId)!;
  const skinId = useStore(skin);
  const paletteId = useStore(palette);
  const skinOption = SKINS.find((o) => o.id === skinId);
  const paletteOption = PALETTES.find((o) => o.id === paletteId);

  useLandOnDefault();

  return (
    <main className="bp-set" data-scroll="y">
      <section className={`card bp-update${ready ? " bp-update-ready" : ""}`} aria-labelledby="bp-update-title" data-nav-group="bp-update">
        <span className="kicker">Updates</span>
        <span className="bp-update-icon" aria-hidden>
          {ready ? <RotateCw /> : <Download />}
        </span>
        <h2 id="bp-update-title" className="bp-update-title">
          {ready && update.version ? `Playdex ${update.version} is ready` : `Playdex ${state.version}`}
        </h2>
        <p className="bp-update-text" role="status">
          {ready ? "Restart Playdex to install it. Your games and settings stay as they are." : update.message}
        </p>
        <UpdateButton update={update} className="btn-lg" isDefault />
      </section>

      <div className="bp-list" data-scroll="y">
        <section className="bp-section" aria-labelledby="bp-app">
          <h2 id="bp-app" className="label">
            This app
          </h2>
          <div className="bp-rows" data-nav-group="bp-app">
            <button className="bp-row" data-nav data-kind="setting" onClick={() => switchMode("desktop").catch(failed)}>
              <AppWindow aria-hidden />
              <span className="bp-row-text">
                <span className="bp-row-name">Desktop mode</span>
                <small>A window and the tray, mouse first. Keys and paths are set there.</small>
              </span>
              <span className="bp-row-action">Switch</span>
            </button>
            <button
              className="bp-row"
              role="switch"
              aria-checked={startWithWindows}
              data-nav
              data-kind="setting"
              onClick={() => changeStartWithWindows(!startWithWindows)}
            >
              <Power aria-hidden />
              <span className="bp-row-text">
                <span className="bp-row-name">Start with Windows</span>
                <small>In the tray, so backups and game launchers stay up to date</small>
              </span>
              <span className="switch" aria-hidden>
                <i />
              </span>
            </button>
            <button className="bp-row" data-nav data-kind="setting" onClick={() => setHomeArt(nextHomeArt(art.id))}>
              <LayoutGrid aria-hidden />
              <span className="bp-row-text">
                <span className="bp-row-name">Home game art</span>
                <small>{art.note}</small>
              </span>
              <span className="bp-row-action">{art.label}</span>
            </button>
            <button className="bp-row" data-nav data-kind="setting" onClick={() => setTheme("skin", nextTheme(SKINS, skinId).id)}>
              <PanelsTopLeft aria-hidden />
              <span className="bp-row-text">
                <span className="bp-row-name">Layout</span>
                <small>{skinOption?.note ?? FROM_URL}</small>
              </span>
              <span className="bp-row-action">{skinOption?.label ?? skinId}</span>
            </button>
            <button className="bp-row" data-nav data-kind="setting" onClick={() => setTheme("palette", nextTheme(PALETTES, paletteId).id)}>
              <Palette aria-hidden />
              <span className="bp-row-text">
                <span className="bp-row-name">Colours</span>
                <small>{paletteOption?.note ?? FROM_URL}</small>
              </span>
              <span className="bp-row-action">
                {paletteOption && <Swatch colors={paletteOption.swatch} />}
                {paletteOption?.label ?? paletteId}
              </span>
            </button>
          </div>
        </section>

        <section className="bp-section" aria-labelledby="bp-connected">
          <h2 id="bp-connected" className="label">
            Status
          </h2>
          {/* Set up in desktop mode: shown flat, never as a tile, so nothing here reads as something to press. */}
          <div className="bp-rows bp-rows-static">
            <StatusRow
              icon={<UserRound aria-hidden />}
              label="Playdex account"
              value={settings.apiKeySet ? `Recording sessions to ${hostOf(settings.baseUrl)}` : "No API key: add one in desktop mode"}
              on={settings.apiKeySet}
            />
            <StatusRow
              icon={<ImageIcon aria-hidden />}
              label="SteamGridDB"
              value={settings.steamGridDbKeySet ? "Artwork for games added to Steam" : "Not set up"}
              on={settings.steamGridDbKeySet}
            />
            <StatusRow
              icon={<Archive aria-hidden />}
              label="Save backups"
              value={settings.ludusaviPath ? "Ludusavi is set up" : "Not set up"}
              on={!!settings.ludusaviPath}
            />
            <StatusRow
              icon={<MonitorPlay aria-hidden />}
              label="Full Screen Experience home"
              value={fse.state === "installed" ? "Installed. Choose Playdex in Settings → Gaming → Full screen experience" : "Set it up in desktop mode"}
              on={fse.state === "installed"}
            />
          </div>
        </section>
      </div>
    </main>
  );
}
