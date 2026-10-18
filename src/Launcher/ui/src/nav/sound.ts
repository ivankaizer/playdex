import { createStore } from "./store";

/*
 * Navigation sounds: short, quiet blips synthesized with Web Audio, so there are no files to ship or decode. A tick
 * when focus moves, a brighter one on confirm, a lower one on back, a soft swish when the tab changes. A per-screen
 * preference (Settings), kept in this browser profile; ?sounds=off in the page URL turns them off for the session.
 *
 * Controller input in the Windows host arrives as host messages, not user gestures; the host starts WebView2 with
 * autoplay allowed so the context plays without a click first.
 */
export type Sound = "move" | "confirm" | "back" | "tab";

const KEY = "playdex.sounds";

function initial(): boolean {
  if (new URLSearchParams(location.search).get("sounds") === "off") return false;
  try {
    return localStorage.getItem(KEY) !== "off";
  } catch {
    // Storage blocked: the default for this session.
    return true;
  }
}

export const soundsOn = createStore<boolean>(initial());

export function setSoundsOn(on: boolean) {
  soundsOn.set(on);
  try {
    if (on) localStorage.removeItem(KEY);
    else localStorage.setItem(KEY, "off");
  } catch {
    // Not remembered; still applies now.
  }
  if (on) play("confirm");
}

/** Frequency (Hz), length (s), peak gain and wave of each sound. Gains stay low: these sit under the UI, not on it. */
const VOICES: Record<Sound, { from: number; to: number; length: number; gain: number; wave: OscillatorType }> = {
  move: { from: 1320, to: 1180, length: 0.035, gain: 0.035, wave: "sine" },
  confirm: { from: 880, to: 1320, length: 0.08, gain: 0.05, wave: "triangle" },
  back: { from: 660, to: 440, length: 0.08, gain: 0.045, wave: "triangle" },
  tab: { from: 520, to: 780, length: 0.06, gain: 0.04, wave: "sine" },
};

/** Held directions repeat fast; ticks closer than this are dropped so a scroll doesn't buzz. */
const MIN_GAP = 0.045;

let ctx: AudioContext | null = null;
let lastAt = -Infinity;

function context(): AudioContext | null {
  if (ctx) return ctx;
  try {
    ctx = new AudioContext();
  } catch {
    return null;
  }
  return ctx;
}

export function play(sound: Sound) {
  if (!soundsOn.get()) return;
  const ac = context();
  if (!ac) return;
  if (ac.state === "suspended") void ac.resume();
  const now = ac.currentTime;
  if (now - lastAt < MIN_GAP) return;
  lastAt = now;

  const v = VOICES[sound];
  const osc = ac.createOscillator();
  const gain = ac.createGain();
  osc.type = v.wave;
  osc.frequency.setValueAtTime(v.from, now);
  osc.frequency.exponentialRampToValueAtTime(v.to, now + v.length);
  // A few milliseconds of attack, then an exponential fall: no click at either end.
  gain.gain.setValueAtTime(0.0001, now);
  gain.gain.exponentialRampToValueAtTime(v.gain, now + 0.004);
  gain.gain.exponentialRampToValueAtTime(0.0001, now + v.length);
  osc.connect(gain).connect(ac.destination);
  osc.start(now);
  osc.stop(now + v.length + 0.02);
}
