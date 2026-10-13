import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

/*
 * The app's own CSS goes in the `app` cascade layer. A theme (src/theme.ts) is plain, unlayered CSS, and unlayered rules
 * beat layered ones whatever their specificity or load order, so a theme never fights the app's selectors and lazily
 * loaded screens can't override it by arriving later.
 */
const appLayer = {
  postcssPlugin: "playdex-app-layer",
  Once(root: any, { AtRule }: any) {
    const file: string = root.source?.input.file ?? "";
    if (!/[\\/]src[\\/]/.test(file) || /node_modules/.test(file)) return;
    const layer = new AtRule({ name: "layer", params: "app" });
    layer.append(root.nodes);
    root.append(layer);
  },
};

export default defineConfig({
  plugins: [react()],
  css: { postcss: { plugins: [appLayer] } },
  server: { port: 5190, strictPort: true },
});
