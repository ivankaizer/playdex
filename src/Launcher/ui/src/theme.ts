/**
 * Themes are stylesheets over the app's own markup: a skin rearranges the layout (public/themes/skins), a palette
 * recolours it through the tokens in styles/tokens.css (public/themes/palettes). The app's CSS is in the `app` cascade
 * layer (vite.config.ts), so a theme's plain rules win without !important or extra specificity.
 *
 * Spike: ?skin=<name> and ?palette=<name> in the page URL pick them and are remembered (=none clears); nothing in the
 * UI picks one yet. Resolves once both stylesheets have loaded (or failed), so the first render already measures with
 * them. The palette is added after the skin, so it wins where both colour the same thing.
 */
const KINDS = [
  ["skin", "skins"],
  ["palette", "palettes"],
] as const;

const NAME = /^[a-z0-9-]+$/;

function chosen(key: string): string | null {
  const fromUrl = new URLSearchParams(location.search).get(key);
  try {
    if (fromUrl === "none") localStorage.removeItem(`pdx.${key}`);
    else if (fromUrl && NAME.test(fromUrl)) localStorage.setItem(`pdx.${key}`, fromUrl);
    const name = fromUrl ?? localStorage.getItem(`pdx.${key}`);
    return name && name !== "none" && NAME.test(name) ? name : null;
  } catch {
    return fromUrl && fromUrl !== "none" && NAME.test(fromUrl) ? fromUrl : null;
  }
}

export function loadTheme(): Promise<void> {
  const loads = KINDS.map(([key, folder]) => {
    const name = chosen(key);
    if (!name) return Promise.resolve();
    document.documentElement.dataset[key] = name;
    return new Promise<void>((done) => {
      const link = document.createElement("link");
      link.rel = "stylesheet";
      link.href = `/themes/${folder}/${name}.css`;
      link.onload = link.onerror = () => done();
      document.head.append(link);
    });
  });
  return Promise.all(loads).then(() => undefined);
}
