import { focusedGame, focusedKind, inputMode } from "./store";

/*
 * Spatial focus engine.
 *
 * Focusables are real DOM elements marked `data-nav`. Moving focus calls element.focus(), so styling is
 * pure CSS (:focus-visible) and React never re-renders a list when focus moves.
 *
 * - `data-nav-group="name"`: items in a group are preferred while moving inside it, and the group remembers
 *   its last focused item so re-entering lands where you left. `data-nav-entry` overrides that: "first" always
 *   enters at the first item, "selected" at the checked or selected one (a radio row lands on the current choice).
 * - `data-nav-default`: the item focused when nothing is focused.
 * - `data-game="id"`: publishes the focused game to the `focusedGame` store.
 * - `data-kind="..."`: publishes a hint context to `focusedKind`.
 * - `data-nav-adjust`: left and right adjust the item (a slider) instead of moving focus; the item gets them as a
 *   cancelable "nav-adjust" event with the direction as `detail`, and focus moves only if it isn't cancelled.
 */

export type Dir = "up" | "down" | "left" | "right";

const memory = new Map<string, HTMLElement>();

function visible(el: HTMLElement) {
  if (!el.isConnected) return false;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
}

function all(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>("[data-nav]")).filter((el) => !el.closest("[inert]") && visible(el));
}

function groupOf(el: Element | null): string | null {
  return el?.closest<HTMLElement>("[data-nav-group]")?.dataset.navGroup ?? null;
}

/* The engine owns focus state (`data-focused`) instead of relying on :focus, which browsers only apply
 * while the window has OS focus. Native focus still follows for assistive tech. */
let cur: HTMLElement | null = null;

export function current(): HTMLElement | null {
  return cur && visible(cur) ? cur : null;
}

function mark(el: HTMLElement) {
  if (cur === el) return;
  cur?.removeAttribute("data-focused");
  el.setAttribute("data-focused", "");
  cur = el;
  const g = groupOf(el);
  if (g) memory.set(g, el);
  const game = el.closest<HTMLElement>("[data-game]")?.dataset.game;
  if (game) focusedGame.set(game);
  focusedKind.set(el.dataset.kind ?? "");
}

/**
 * Where an item sits without its own transform. A focused tile lifts and grows (translate + scale), so its box starts
 * above the tiles beside it; measured as drawn, a neighbour in the same row would count as below it.
 */
function layoutRect(el: HTMLElement): DOMRect {
  const r = el.getBoundingClientRect();
  const style = getComputedStyle(el);
  if (style.transform === "none") return r;
  const { a, d, e, f } = new DOMMatrixReadOnly(style.transform);
  if (!a || !d) return r;
  const [ox, oy] = style.transformOrigin.split(" ").map(parseFloat);
  const width = r.width / a;
  const height = r.height / d;
  // Drawn left = layout left + ox·(1 − a) + e, for a scale and translate about the origin.
  return new DOMRect(r.left - (ox || 0) * (1 - a) - e, r.top - (oy || 0) * (1 - d) - f, width, height);
}

/**
 * Score a candidate: distance along the axis plus a heavy penalty for being off-axis. Off-axis is measured between
 * centres, or between start edges when leaving a wider item: down from a tile spanning a row lands on the row's first
 * item, as in reading order, not the one under its middle.
 */
function score(from: DOMRect, to: DOMRect, dir: Dir): number | null {
  const tol = 2;
  let primary: number;
  let overlap: number;
  let offAxis: number;
  if (dir === "right" || dir === "left") {
    primary = dir === "right" ? to.left - from.right : from.left - to.right;
    if (dir === "right" ? to.left < from.left + tol : to.right > from.right - tol) return null;
    overlap = Math.min(from.bottom, to.bottom) - Math.max(from.top, to.top);
    offAxis = from.height > to.height + tol ? Math.abs(from.top - to.top) : Math.abs(from.top + from.height / 2 - (to.top + to.height / 2));
  } else {
    primary = dir === "down" ? to.top - from.bottom : from.top - to.bottom;
    if (dir === "down" ? to.top < from.top + tol : to.bottom > from.bottom - tol) return null;
    overlap = Math.min(from.right, to.right) - Math.max(from.left, to.left);
    offAxis = from.width > to.width + tol ? Math.abs(from.left - to.left) : Math.abs(from.left + from.width / 2 - (to.left + to.width / 2));
  }
  primary = Math.max(0, primary);
  // Overlapping candidates win; otherwise misalignment costs three times distance.
  return overlap > 0 ? primary + offAxis * 0.25 : primary + offAxis * 3 + 10_000;
}

export function focusEl(el: HTMLElement | null | undefined) {
  if (!el) return;
  mark(el);
  el.focus({ preventScroll: true });
  reveal(el);
}

/**
 * The axis a scroll container scrolls on, from its computed overflow, so a theme that turns a shelf into a column still
 * scrolls it. When both axes may scroll, the one with content beyond the box; `data-scroll` decides a tie.
 */
function scrollAxis(sc: HTMLElement): "x" | "y" {
  const s = getComputedStyle(sc);
  const x = s.overflowX === "auto" || s.overflowX === "scroll";
  const y = s.overflowY === "auto" || s.overflowY === "scroll";
  if (x !== y) return x ? "x" : "y";
  if (x) {
    const overX = sc.scrollWidth > sc.clientWidth;
    const overY = sc.scrollHeight > sc.clientHeight;
    if (overX !== overY) return overX ? "x" : "y";
  }
  return sc.dataset.scroll === "x" ? "x" : "y";
}

/**
 * Keep the focused item in view inside each scroll container around it (horizontal shelves, vertical lists): a row of
 * pills scrolls sideways inside a panel that scrolls down.
 */
function reveal(el: HTMLElement) {
  for (let sc = el.closest<HTMLElement>("[data-scroll]"); sc; sc = sc.parentElement?.closest<HTMLElement>("[data-scroll]") ?? null) {
    revealIn(sc, el);
  }
}

function revealIn(sc: HTMLElement, el: HTMLElement) {
  const axis = scrollAxis(sc);
  const r = el.getBoundingClientRect();
  const c = sc.getBoundingClientRect();
  const pad = parseFloat(getComputedStyle(sc).scrollPaddingInlineStart || "0") || 24;
  if (axis === "x") {
    if (r.left < c.left + pad) sc.scrollBy({ left: r.left - c.left - pad, behavior: "smooth" });
    else if (r.right > c.right - pad) sc.scrollBy({ left: r.right - c.right + pad, behavior: "smooth" });
  } else if (sc.dataset.scrollCenter !== undefined) {
    // Grids keep the focused row centred, so the rows above and below stay in view.
    sc.scrollBy({ top: r.top + r.height / 2 - (c.top + c.height / 2), behavior: "smooth" });
  } else if (el === sc.querySelector("[data-nav]")) {
    // The first and last items take a list to its ends, so a heading above them, or rows below that are not controls
    // (Big Picture's status), come into view.
    sc.scrollTo({ top: 0, behavior: "smooth" });
  } else if (el === Array.from(sc.querySelectorAll("[data-nav]")).at(-1)) {
    sc.scrollTo({ top: sc.scrollHeight, behavior: "smooth" });
  } else {
    if (r.top < c.top + pad) sc.scrollBy({ top: r.top - c.top - pad, behavior: "smooth" });
    else if (r.bottom > c.bottom - pad) sc.scrollBy({ top: r.bottom - c.bottom + pad, behavior: "smooth" });
  }
}

export function focusDefault() {
  const els = all();
  focusEl(els.find((e) => e.hasAttribute("data-nav-default")) ?? els[0]);
}

/**
 * A new screen's first focus: its default item for a controller or the keyboard. With the mouse, focus stays on what
 * was clicked (the tab that opened the screen) instead of jumping into the screen, or to the first item on the page
 * while a lazily loaded screen is still empty.
 */
export function focusScreenDefault() {
  if (inputMode.get() === "pointer" && current()) return;
  focusDefault();
}

export function move(dir: Dir) {
  const cur = current();
  if (!cur) return focusDefault();
  if (
    (dir === "left" || dir === "right") &&
    cur.hasAttribute("data-nav-adjust") &&
    !cur.dispatchEvent(new CustomEvent<Dir>("nav-adjust", { detail: dir, cancelable: true }))
  ) {
    return;
  }
  const from = layoutRect(cur);
  const curGroup = groupOf(cur);
  let best: HTMLElement | null = null;
  let bestScore = Infinity;
  let bestSame: HTMLElement | null = null;
  let bestSameScore = Infinity;
  for (const el of all()) {
    if (el === cur) continue;
    const s = score(from, layoutRect(el), dir);
    if (s === null) continue;
    if (s < bestScore) [best, bestScore] = [el, s];
    if (curGroup && groupOf(el) === curGroup && s < bestSameScore && s < 10_000) [bestSame, bestSameScore] = [el, s];
  }
  let target = bestSame ?? best;
  if (!target) return;
  // Entering another group: return to the item it remembers, or its first item.
  const g = groupOf(target);
  if (g && g !== curGroup) {
    const group = target.closest<HTMLElement>("[data-nav-group]")!;
    const entry =
      group.dataset.navEntry === "first"
        ? group.querySelector<HTMLElement>("[data-nav]")
        : group.dataset.navEntry === "selected"
          ? group.querySelector<HTMLElement>('[data-nav][aria-checked="true"], [data-nav][aria-selected="true"]')
          : null;
    const remembered = memory.get(g);
    if (entry && visible(entry)) target = entry;
    else if (remembered && visible(remembered)) target = remembered;
  }
  focusEl(target);
}

/** Focus that arrives from outside the engine (Tab key, clicks) is adopted. */
document.addEventListener("focusin", (e) => {
  const el = e.target as HTMLElement;
  if (el.hasAttribute?.("data-nav")) mark(el);
});

export function forgetGroups() {
  memory.clear();
}
