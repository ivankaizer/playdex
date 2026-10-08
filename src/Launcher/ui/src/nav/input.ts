import { current, focusEl, move, type Dir } from "./focus";
import { inputMode } from "./store";

/*
 * Input: keyboard and gamepad map onto the same actions. In the Windows host, controller events arrive from
 * a native XInput thread; this browser Gamepad API path is the prototype stand-in.
 */

export type Action = "confirm" | "back" | "x" | "y" | "lb" | "rb" | "view" | "menu";

const handlers = new Set<(a: Action) => boolean | void>();

/** Subscribe to actions. Latest subscriber runs first; return true to stop propagation. */
export function onAction(fn: (a: Action) => boolean | void) {
  handlers.add(fn);
  return () => {
    handlers.delete(fn);
  };
}

function dispatch(a: Action) {
  for (const fn of [...handlers].reverse()) if (fn(a)) return;
}

function confirm() {
  current()?.click();
}

function setMode(m: "pad" | "pointer") {
  if (inputMode.get() === m) return;
  inputMode.set(m);
  document.documentElement.dataset.input = m;
}

const KEY_DIR: Record<string, Dir> = { ArrowUp: "up", ArrowDown: "down", ArrowLeft: "left", ArrowRight: "right" };
const KEY_ACTION: Record<string, Action> = {
  Escape: "back",
  Backspace: "back",
  q: "lb",
  e: "rb",
  x: "x",
  y: "y",
  v: "view",
  m: "menu",
};

window.addEventListener("keydown", (e) => {
  if ((e.target as Element).closest?.("input, textarea")) return;
  const dir = KEY_DIR[e.key];
  if (dir) {
    e.preventDefault();
    setMode("pad");
    move(dir);
    return;
  }
  if (e.key === "Enter" || e.key === " ") {
    e.preventDefault();
    setMode("pad");
    confirm();
    return;
  }
  const a = KEY_ACTION[e.key];
  if (a) {
    e.preventDefault();
    setMode("pad");
    dispatch(a);
  }
});

window.addEventListener("mousemove", () => setMode("pointer"), { passive: true });

/* Pointer: hovering a focusable focuses it, so mouse and pad share one highlight. */
document.addEventListener("pointerover", (e) => {
  if (e.pointerType !== "mouse") return;
  const el = (e.target as HTMLElement).closest<HTMLElement>("[data-nav]");
  if (el && current() !== el) focusEl(el);
});

/* ---- Gamepad ---- */

// Standard mapping indices.
const BTN = { A: 0, B: 1, X: 2, Y: 3, LB: 4, RB: 5, VIEW: 8, MENU: 9, UP: 12, DOWN: 13, LEFT: 14, RIGHT: 15 };
const BTN_ACTION: [number, Action][] = [
  [BTN.B, "back"],
  [BTN.X, "x"],
  [BTN.Y, "y"],
  [BTN.LB, "lb"],
  [BTN.RB, "rb"],
  [BTN.VIEW, "view"],
  [BTN.MENU, "menu"],
];

const REPEAT_DELAY = 400;
const REPEAT_START = 120;
const REPEAT_MIN = 60;

const prev = new Map<number, boolean>();
let heldDir: Dir | null = null;
let nextRepeat = 0;
let repeatGap = REPEAT_START;

function stickDir(pad: Gamepad): Dir | null {
  const [x = 0, y = 0] = pad.axes;
  if (Math.max(Math.abs(x), Math.abs(y)) < 0.5) return null;
  return Math.abs(x) > Math.abs(y) ? (x > 0 ? "right" : "left") : y > 0 ? "down" : "up";
}

function poll(t: number) {
  const pad = navigator.getGamepads?.().find((p) => p && p.connected);
  if (pad && document.hasFocus()) {
    const pressed = (i: number) => !!pad.buttons[i]?.pressed;
    const edge = (i: number) => {
      const now = pressed(i);
      const was = prev.get(i) ?? false;
      prev.set(i, now);
      return now && !was;
    };

    const dir: Dir | null = pressed(BTN.UP)
      ? "up"
      : pressed(BTN.DOWN)
        ? "down"
        : pressed(BTN.LEFT)
          ? "left"
          : pressed(BTN.RIGHT)
            ? "right"
            : stickDir(pad);

    if (dir !== heldDir) {
      heldDir = dir;
      if (dir) {
        setMode("pad");
        move(dir);
        nextRepeat = t + REPEAT_DELAY;
        repeatGap = REPEAT_START;
      }
    } else if (dir && t >= nextRepeat) {
      move(dir);
      // Held directions accelerate the longer they are held.
      repeatGap = Math.max(REPEAT_MIN, repeatGap * 0.88);
      nextRepeat = t + repeatGap;
    }

    if (edge(BTN.A)) {
      setMode("pad");
      confirm();
    }
    for (const [i, a] of BTN_ACTION) if (edge(i)) {
      setMode("pad");
      dispatch(a);
    }
  }
  requestAnimationFrame(poll);
}

window.addEventListener("gamepadconnected", () => setMode("pad"));
requestAnimationFrame(poll);
