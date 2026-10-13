import { Activity, AppWindow, Check, ExternalLink, Gamepad2, Gauge, Headphones, Monitor, Moon, Power, RotateCcw, Speaker, Sun } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { closeQuickSettings, openSteam, showToast } from "../app-state";
import {
  inHost,
  onHostMessage,
  postToHost,
  type AudioOutput,
  type PowerAction,
  type QuickSettingsState,
} from "../host/bridge";
import { isDesktop, switchMode } from "../host/desktop";
import { focusDefault } from "../nav/focus";
import { onAction } from "../nav/input";
import { Hint } from "./shared";
import "./quick-settings.css";

/* In a plain browser there is no host: a stand-in state, and changes only update the panel. */
const DEMO: QuickSettingsState = {
  outputs: [
    { id: "tv", name: "LG TV (NVIDIA High Definition Audio)", isDefault: true },
    { id: "headset", name: "Headphones (Xbox Wireless Headset)", isDefault: false },
    { id: "speakers", name: "Speakers (Realtek(R) Audio)", isDefault: false },
  ],
  displays: [{ id: "tv", name: "LG TV", hdrSupported: true, hdrOn: false, refreshRate: 60, refreshRates: [24, 30, 50, 60, 100, 120] }],
  fpsCounter: { hotkey: "Alt+R" },
};

const POWER: { action: PowerAction; label: string; armed: string; done: string; Icon: typeof Power }[] = [
  { action: "sleep", label: "Sleep", armed: "Sleep now?", done: "Going to sleep…", Icon: Moon },
  { action: "restart", label: "Restart", armed: "Restart now?", done: "Restarting…", Icon: RotateCcw },
  { action: "shutdown", label: "Shut down", armed: "Shut down now?", done: "Shutting down…", Icon: Power },
];

/** How long a power button stays armed for its confirming second press. */
const ARM_MS = 4000;

/** "Speakers (Realtek(R) Audio)" → name "Speakers", detail "Realtek(R) Audio". */
function splitName(name: string): { name: string; detail?: string } {
  const open = name.indexOf(" (");
  return open > 0 && name.endsWith(")") ? { name: name.slice(0, open), detail: name.slice(open + 2, -1) } : { name };
}

function OutputIcon({ output }: { output: AudioOutput }) {
  const n = output.name.toLowerCase();
  if (/head(phone|set)|earbud|airpods|buds/.test(n)) return <Headphones aria-hidden />;
  if (/hdmi|display|monitor|tv\b|nvidia|amd high definition|intel\(r\) display/.test(n)) return <Monitor aria-hidden />;
  return <Speaker aria-hidden />;
}

/**
 * Quick settings: the default sound output, HDR and refresh rate per display, the NVIDIA FPS counter, switching between
 * Big Picture and desktop mode, and sleep / restart / shut down. A panel over the current
 * screen (which is inert while it is open); B or Menu closes it. Power actions take a second press to confirm.
 */
export function QuickSettings() {
  const [state, setState] = useState<QuickSettingsState | null>(inHost ? null : DEMO);
  const [armed, setArmed] = useState<PowerAction | null>(null);
  const focused = useRef(false);

  useEffect(() => {
    const off = onHostMessage((m) => {
      if (m.type === "home") {
        closeQuickSettings();
        return;
      }
      if (m.type !== "quick-settings") return;
      setState({ outputs: m.outputs, displays: m.displays, fpsCounter: m.fpsCounter });
      if (m.error) showToast(m.error);
    });
    postToHost({ type: "quick-settings" });
    return off;
  }, []);

  // Land on the current sound output once the state is in (or on the first control if the host is slow).
  useEffect(() => {
    if (focused.current) return;
    const land = () => {
      if (focused.current) return;
      focused.current = true;
      focusDefault();
    };
    if (state) {
      const t = window.setTimeout(land);
      return () => window.clearTimeout(t);
    }
    const t = window.setTimeout(land, 400);
    return () => window.clearTimeout(t);
  }, [state]);

  useEffect(() => {
    if (!armed) return;
    const t = window.setTimeout(() => setArmed(null), ARM_MS);
    return () => window.clearTimeout(t);
  }, [armed]);

  // The panel owns the controller while open: B and Menu close it, Guide closes it and goes home, the rest is ignored.
  useEffect(
    () =>
      onAction((a) => {
        if (a === "back" || a === "menu") {
          closeQuickSettings();
          return true;
        }
        if (a === "guide") {
          closeQuickSettings();
          return false;
        }
        return true;
      }),
    [],
  );

  function chooseOutput(id: string) {
    setArmed(null);
    setState((s) => s && { ...s, outputs: s.outputs.map((o) => ({ ...o, isDefault: o.id === id })) });
    postToHost({ type: "audio-output", id });
  }

  function toggleHdr(id: string, on: boolean) {
    setArmed(null);
    setState((s) => s && { ...s, displays: s.displays.map((d) => (d.id === id ? { ...d, hdrOn: on } : d)) });
    postToHost({ type: "hdr", display: id, on });
  }

  function setRefreshRate(id: string, hz: number) {
    setArmed(null);
    setState((s) => s && { ...s, displays: s.displays.map((d) => (d.id === id ? { ...d, refreshRate: hz } : d)) });
    postToHost({ type: "refresh-rate", display: id, hz });
  }

  function toggleFpsCounter() {
    setArmed(null);
    if (!inHost) {
      showToast("The FPS counter works in the Playdex app on Windows");
      return;
    }
    postToHost({ type: "fps-counter" });
  }

  function changeMode() {
    setArmed(null);
    closeQuickSettings();
    switchMode(isDesktop ? "bigPicture" : "desktop").catch((e: unknown) => showToast(e instanceof Error ? e.message : String(e)));
  }

  function power(action: PowerAction) {
    if (armed !== action) {
      setArmed(action);
      return;
    }
    const p = POWER.find((x) => x.action === action)!;
    closeQuickSettings();
    if (!inHost) {
      showToast(`${p.label} works in the Playdex app on Windows`);
      return;
    }
    showToast(p.done);
    postToHost({ type: "power", action });
  }

  return (
    <div className="qs-layer" role="dialog" aria-modal="true" aria-label="Quick settings">
      <div className="qs-scrim" onClick={closeQuickSettings} />
      <aside className="qs" data-scroll="y">
        <span className="kicker">Quick settings</span>

        <section className="qs-section" aria-labelledby="qs-sound">
          <h2 id="qs-sound" className="label">
            Sound output
          </h2>
          <div className="qs-list" role="radiogroup" aria-labelledby="qs-sound" data-nav-group="qs-sound">
            {!state ? (
              <div className="qs-row qs-placeholder" />
            ) : state.outputs.length === 0 ? (
              <p className="qs-empty">No sound outputs found.</p>
            ) : (
              state.outputs.map((o) => {
                const { name, detail } = splitName(o.name);
                return (
                  <button
                    key={o.id}
                    className="qs-row"
                    role="radio"
                    aria-checked={o.isDefault}
                    data-nav
                    data-kind="qs-output"
                    data-nav-default={o.isDefault ? "" : undefined}
                    onClick={() => chooseOutput(o.id)}
                  >
                    <OutputIcon output={o} />
                    <span className="qs-row-text">
                      <span className="qs-row-name">{name}</span>
                      {detail && <small>{detail}</small>}
                    </span>
                    {o.isDefault && <Check className="qs-check" aria-hidden />}
                  </button>
                );
              })
            )}
          </div>
        </section>

        <section className="qs-section" aria-labelledby="qs-display">
          <h2 id="qs-display" className="label">
            Display
          </h2>
          {!state ? (
            <div className="qs-row qs-placeholder" />
          ) : state.displays.length === 0 ? (
            <p className="qs-empty">No displays found.</p>
          ) : (
            state.displays.map((d) => (
              <div key={d.id} className="qs-list">
                <div data-nav-group={`qs-hdr-${d.id}`}>
                  {d.hdrSupported ? (
                    <button
                      className="qs-row"
                      role="switch"
                      aria-checked={d.hdrOn}
                      data-nav
                      data-kind="qs-hdr"
                      onClick={() => toggleHdr(d.id, !d.hdrOn)}
                    >
                      <Sun aria-hidden />
                      <span className="qs-row-text">
                        <span className="qs-row-name">HDR</span>
                        <small>{d.name}</small>
                      </span>
                      <span className="qs-switch" aria-hidden>
                        <i />
                      </span>
                    </button>
                  ) : (
                    <div className="qs-row qs-row-off">
                      <Sun aria-hidden />
                      <span className="qs-row-text">
                        <span className="qs-row-name">HDR</span>
                        <small>{d.name} · not supported</small>
                      </span>
                    </div>
                  )}
                </div>
                {d.refreshRates.length > 1 && (
                  <div className="qs-rate">
                    <div className="qs-rate-head">
                      <Gauge aria-hidden />
                      <span className="qs-row-text">
                        <span className="qs-row-name" id={`qs-rate-${d.id}`}>
                          Refresh rate
                        </span>
                        <small>{d.name}</small>
                      </span>
                    </div>
                    <div
                      className="qs-rate-options"
                      role="radiogroup"
                      aria-labelledby={`qs-rate-${d.id}`}
                      data-scroll="x"
                      data-nav-group={`qs-rate-${d.id}`}
                      data-nav-entry="selected"
                    >
                      {d.refreshRates.map((hz) => (
                        <button
                          key={hz}
                          className="qs-pill num"
                          role="radio"
                          aria-checked={hz === d.refreshRate}
                          data-nav
                          data-kind="qs-rate"
                          onClick={() => setRefreshRate(d.id, hz)}
                        >
                          {hz} Hz
                        </button>
                      ))}
                    </div>
                  </div>
                )}
              </div>
            ))
          )}
        </section>

        {state?.fpsCounter && (
          <section className="qs-section" aria-labelledby="qs-performance">
            <h2 id="qs-performance" className="label">
              Performance
            </h2>
            {/* NVIDIA's overlay keeps its on/off state to itself, so this is a toggle, not a switch. */}
            <div className="qs-list" data-nav-group="qs-performance">
              <button className="qs-row" data-nav data-kind="qs-fps" onClick={toggleFpsCounter}>
                <Activity aria-hidden />
                <span className="qs-row-text">
                  <span className="qs-row-name">FPS counter</span>
                  <small>NVIDIA overlay · {state.fpsCounter.hotkey}</small>
                </span>
                <span className="qs-row-action">Show / hide</span>
              </button>
            </div>
          </section>
        )}

        <section className="qs-section" aria-labelledby="qs-mode">
          <h2 id="qs-mode" className="label">
            Playdex
          </h2>
          <div className="qs-list" data-nav-group="qs-mode">
            <button className="qs-row" data-nav data-kind="qs-mode" onClick={changeMode}>
              {isDesktop ? <Gamepad2 aria-hidden /> : <AppWindow aria-hidden />}
              <span className="qs-row-text">
                <span className="qs-row-name">{isDesktop ? "Big Picture" : "Desktop mode"}</span>
                <small>{isDesktop ? "Full screen, controller first" : "A window and the tray, mouse first"}</small>
              </span>
              <span className="qs-row-action">Switch</span>
            </button>
            <button
              className="qs-row"
              data-nav
              data-kind="qs-steam"
              onClick={() => {
                closeQuickSettings();
                openSteam();
              }}
            >
              <ExternalLink aria-hidden />
              <span className="qs-row-text">
                <span className="qs-row-name">Steam</span>
                <small>{isDesktop ? "The Steam window" : "Steam Big Picture"}</small>
              </span>
              <span className="qs-row-action">Open</span>
            </button>
          </div>
        </section>

        <section className="qs-section" aria-labelledby="qs-power">
          <h2 id="qs-power" className="label">
            Power
          </h2>
          {/* Entering the row lands on Sleep, the gentlest action, not on whichever tile is nearest. */}
          <div className="qs-power" data-nav-group="qs-power" data-nav-entry="first">
            {POWER.map(({ action, label, armed: armedLabel, Icon }) => (
              <button
                key={action}
                className="qs-power-btn"
                data-nav
                data-kind="qs-power"
                data-armed={armed === action ? "" : undefined}
                aria-label={armed === action ? `${armedLabel} Press again to confirm` : label}
                onClick={() => power(action)}
              >
                <Icon aria-hidden />
                <span>{armed === action ? armedLabel : label}</span>
              </button>
            ))}
          </div>
          <p className="qs-note" aria-live="polite">
            {armed ? "Press again to confirm." : " "}
          </p>
        </section>

        <footer className="hints qs-hints">
          <Hint glyph="A">Select</Hint>
          <Hint glyph="B">Close</Hint>
        </footer>
      </aside>
    </div>
  );
}
