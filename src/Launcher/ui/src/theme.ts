/**
 * A theme is one stylesheet over the app's own markup. The app's CSS is in the `app` cascade layer (vite.config.ts),
 * so a theme's plain rules win without !important or extra specificity.
 *
 * Spike: ?theme=<name> in the page URL loads /themes/<name>.css (public/themes); nothing in the UI picks or saves one
 * yet. Resolves once the stylesheet has loaded (or failed), so the first render already measures with it.
 */
export function loadTheme(): Promise<void> {
  const name = new URLSearchParams(location.search).get("theme");
  if (!name || !/^[a-z0-9-]+$/.test(name)) return Promise.resolve();
  document.documentElement.dataset.themePack = name;
  return new Promise((done) => {
    const link = document.createElement("link");
    link.rel = "stylesheet";
    link.href = `/themes/${name}.css`;
    link.onload = link.onerror = () => done();
    document.head.append(link);
  });
}
