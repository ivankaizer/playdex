import { isDesktop } from "./host/desktop";
import { createStore } from "./nav/store";

/**
 * Themes are stylesheets over the app's own markup: a skin rearranges the layout (public/themes/skins), a palette
 * recolours it through the tokens in styles/tokens.css (public/themes/palettes). The app's CSS is in the `app` cascade
 * layer (vite.config.ts), so a theme's plain rules win without !important or extra specificity.
 *
 * Settings → Appearance picks them, kept in this browser profile. ?skin=<name> and ?palette=<name> in the page URL
 * pick one too, including one not listed below (for writing a new theme); =none goes back to the default.
 *
 * Skins rearrange Big Picture's markup, so desktop mode, which has its own layout, keeps the choice but loads none.
 * Palettes and the light or dark scheme apply in both modes.
 */
export type ThemeKind = "skin" | "palette";

export type ThemeOption = { id: string; label: string; note: string };
export type PaletteOption = ThemeOption & {
  /** Page, accent and second accent, for the picker's swatch. */
  swatch: [string, string, string];
};

/** The default of each kind is the app's own CSS; it loads no stylesheet. */
export const DEFAULT_THEME = "default";

export const SKINS: ThemeOption[] = [
  { id: DEFAULT_THEME, label: "Playdex", note: "Tabs along the top, the last played game leading Home." },
  { id: "sidebar", label: "Sidebar", note: "Tabs down the left, Home's games as a list." },
  { id: "cinema", label: "Cinema", note: "The last played game's art fills the screen." },
  { id: "dock", label: "Dock", note: "Tabs in a dock at the bottom, Home as a grid of tiles." },
];

export const PALETTES: PaletteOption[] = [
  { id: DEFAULT_THEME, label: "Playdex", note: "Mint, cyan and sand.", swatch: ["#0f151b", "#6fe3b5", "#56bee2"] },
  { id: "oled", label: "OLED", note: "True black, for OLED screens. Dark only.", swatch: ["#000000", "#6fe3b5", "#56bee2"] },
  { id: "nord", label: "Nord", note: "Arctic blue-greys and frost.", swatch: ["#2e3440", "#88c0d0", "#ebcb8b"] },
  { id: "dracula", label: "Dracula", note: "Purple and pink on night grey.", swatch: ["#282a36", "#bd93f9", "#ff79c6"] },
  { id: "catppuccin", label: "Catppuccin", note: "Soft pastels, mauve first.", swatch: ["#1e1e2e", "#cba6f7", "#89dceb"] },
  { id: "gruvbox", label: "Gruvbox", note: "Warm retro browns, yellow and orange.", swatch: ["#282828", "#fabd2f", "#fe8019"] },
  { id: "tokyo-night", label: "Tokyo Night", note: "City-at-night blues and violets.", swatch: ["#1a1b26", "#7aa2f7", "#bb9af7"] },
  { id: "rose-pine", label: "Rosé Pine", note: "Muted rose, gold and pine.", swatch: ["#191724", "#ebbcba", "#f6c177"] },
  { id: "synthwave", label: "Synthwave", note: "Neon pink and cyan on violet. Dark only.", swatch: ["#1a1030", "#ff4fd8", "#36f9f6"] },
  { id: "pocket", label: "Pocket", note: "The four greens of a 1989 handheld's screen.", swatch: ["#9bbc0f", "#0f380f", "#8bac0f"] },
  { id: "high-contrast", label: "High contrast", note: "Black and white, yellow focus, heavier rings.", swatch: ["#000000", "#ffd400", "#ffffff"] },
];

const FOLDER: Record<ThemeKind, string> = { skin: "skins", palette: "palettes" };
const NAME = /^[a-z0-9-]+$/;

function initial(kind: ThemeKind): string {
  const fromUrl = new URLSearchParams(location.search).get(kind);
  if (fromUrl === "none") return DEFAULT_THEME;
  if (fromUrl && NAME.test(fromUrl)) return fromUrl;
  try {
    const saved = localStorage.getItem(`pdx.${kind}`);
    if (saved && NAME.test(saved)) return saved;
  } catch {
    // Storage blocked: the default for this session.
  }
  return DEFAULT_THEME;
}

export const skin = createStore<string>(initial("skin"));
export const palette = createStore<string>(initial("palette"));
const stores = { skin, palette };

const links: Partial<Record<ThemeKind, HTMLLinkElement>> = {};

/**
 * Puts the kind's stylesheet in place. The one it replaces stays until the new one has loaded, so a switch never shows
 * the page unthemed. A skin's sheet goes before the palette's, so a palette wins where both colour the same thing.
 */
function apply(kind: ThemeKind, id: string): Promise<void> {
  const old = links[kind];
  const root = document.documentElement;
  if (id === DEFAULT_THEME) {
    old?.remove();
    delete links[kind];
    delete root.dataset[kind];
    return Promise.resolve();
  }
  root.dataset[kind] = id;
  const link = document.createElement("link");
  link.rel = "stylesheet";
  link.href = `/themes/${FOLDER[kind]}/${id}.css`;
  links[kind] = link;
  if (old) old.after(link);
  else if (kind === "skin" && links.palette) links.palette.before(link);
  else document.head.append(link);
  return new Promise((done) => {
    link.onload = link.onerror = () => {
      old?.remove();
      done();
    };
  });
}

/** Switches a theme now and remembers it. */
export function setTheme(kind: ThemeKind, id: string) {
  if (!NAME.test(id) || stores[kind].get() === id) return;
  stores[kind].set(id);
  try {
    if (id === DEFAULT_THEME) localStorage.removeItem(`pdx.${kind}`);
    else localStorage.setItem(`pdx.${kind}`, id);
  } catch {
    // Not remembered; still applies now.
  }
  if (kind === "skin" && isDesktop) return;
  void apply(kind, id);
}

/** The chosen themes at startup. Resolves once their stylesheets have loaded (or failed), so the first render measures with them. */
export function loadTheme(): Promise<void> {
  return Promise.all([apply("skin", isDesktop ? DEFAULT_THEME : skin.get()), apply("palette", palette.get())]).then(() => undefined);
}

/* ---------- Light or dark ---------- */

export type Scheme = "dark" | "light";

function initialScheme(): Scheme {
  const fromUrl = new URLSearchParams(location.search).get("theme");
  let saved: string | null = null;
  try {
    saved = localStorage.getItem("pdx.theme");
  } catch {
    // Storage blocked: the default for this session.
  }
  const v = fromUrl ?? saved;
  return v === "light" ? "light" : "dark";
}

/** The light or dark variant of the palette (`data-theme` on the root), shared by both modes. */
export const scheme = createStore<Scheme>(initialScheme());
document.documentElement.dataset.theme = scheme.get();

export function setScheme(next: Scheme) {
  scheme.set(next);
  document.documentElement.dataset.theme = next;
  try {
    localStorage.setItem("pdx.theme", next);
  } catch {
    // Not remembered; still applies now.
  }
}

/** The next choice, for a single button that cycles through them (Big Picture). */
export function nextTheme<T extends ThemeOption>(options: T[], id: string): T {
  return options[(options.findIndex((o) => o.id === id) + 1) % options.length];
}
