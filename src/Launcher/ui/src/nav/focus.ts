import { focusedGame, focusedKind } from "./store";

/*
 * Spatial focus engine.
 *
 * Focusables are real DOM elements marked `data-nav`. Moving focus calls element.focus(), so styling is
 * pure CSS (:focus-visible) and React never re-renders a list when focus moves.
 *
 * - `data-nav-group="name"`: items in a group are preferred while moving inside it, and the group remembers
 *   its last focused item so re-entering lands where you left.
 * - `data-nav-default`: the item focused when nothing is focused.
 * - `data-game="id"`: publishes the focused game to the `focusedGame` store.
 * - `data-kind="..."`: publishes a hint context to `focusedKind`.
 */

export type Dir = "up" | "down" | "left" | "right";

const memory = new Map<string, HTMLElement>();

function visible(el: HTMLElement) {
  if (!el.isConnected) return false;
  const r = el.getBoundingClientRect();
  return r.width > 0 && r.height > 0;
}

function all(): HTMLElement[] {
  return Array.from(document.querySelectorAll<HTMLElement>("[data-nav]")).filter(
    (el) => !el.closest("[inert]") && visible(el),
  );
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

/** Score a candidate: distance along the axis plus a heavy penalty for being off-axis. */
function score(from: DOMRect, to: DOMRect, dir: Dir): number | null {
  const tol = 2;
  let primary: number;
  let overlap: number;
  let offAxis: number;
  if (dir === "right" || dir === "left") {
    primary = dir === "right" ? to.left - from.right : from.left - to.right;
    if (dir === "right" ? to.left < from.left + tol : to.right > from.right - tol) return null;
    overlap = Math.min(from.bottom, to.bottom) - Math.max(from.top, to.top);
    offAxis = Math.abs(from.top + from.height / 2 - (to.top + to.height / 2));
  } else {
    primary = dir === "down" ? to.top - from.bottom : from.top - to.bottom;
    if (dir === "down" ? to.top < from.top + tol : to.bottom > from.bottom - tol) return null;
    overlap = Math.min(from.right, to.right) - Math.max(from.left, to.left);
    offAxis = Math.abs(from.left + from.width / 2 - (to.left + to.width / 2));
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

/** Keep the focused item in view inside its scroll container (horizontal shelves, vertical lists). */
function reveal(el: HTMLElement) {
  const sc = el.closest<HTMLElement>("[data-scroll]");
  if (!sc) return;
  const axis = sc.dataset.scroll;
  const r = el.getBoundingClientRect();
  const c = sc.getBoundingClientRect();
  const pad = parseFloat(getComputedStyle(sc).scrollPaddingInlineStart || "0") || 24;
  if (axis === "x") {
    if (r.left < c.left + pad) sc.scrollBy({ left: r.left - c.left - pad, behavior: "smooth" });
    else if (r.right > c.right - pad) sc.scrollBy({ left: r.right - c.right + pad, behavior: "smooth" });
  } else if (sc.dataset.scrollCenter !== undefined) {
    // Grids keep the focused row centred, so the rows above and below stay in view.
    sc.scrollBy({ top: r.top + r.height / 2 - (c.top + c.height / 2), behavior: "smooth" });
  } else {
    if (r.top < c.top + pad) sc.scrollBy({ top: r.top - c.top - pad, behavior: "smooth" });
    else if (r.bottom > c.bottom - pad) sc.scrollBy({ top: r.bottom - c.bottom + pad, behavior: "smooth" });
  }
}

export function focusDefault() {
  const els = all();
  focusEl(els.find((e) => e.hasAttribute("data-nav-default")) ?? els[0]);
}

export function move(dir: Dir) {
  const cur = current();
  if (!cur) return focusDefault();
  const from = cur.getBoundingClientRect();
  const curGroup = groupOf(cur);
  let best: HTMLElement | null = null;
  let bestScore = Infinity;
  let bestSame: HTMLElement | null = null;
  let bestSameScore = Infinity;
  for (const el of all()) {
    if (el === cur) continue;
    const s = score(from, el.getBoundingClientRect(), dir);
    if (s === null) continue;
    if (s < bestScore) [best, bestScore] = [el, s];
    if (curGroup && groupOf(el) === curGroup && s < bestSameScore && s < 10_000) [bestSame, bestSameScore] = [el, s];
  }
  let target = bestSame ?? best;
  if (!target) return;
  // Entering another group: return to the item it remembers.
  const g = groupOf(target);
  if (g && g !== curGroup) {
    const remembered = memory.get(g);
    if (remembered && visible(remembered)) target = remembered;
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
