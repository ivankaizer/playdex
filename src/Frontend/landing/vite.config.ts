/* The landing (playdex.dev) as its own static site: built from landing/ into landing/dist and deployed to
   Cloudflare Pages by .github/workflows/landing.yml. It reuses the app's components through the same "@" alias. */
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath, URL } from "node:url";

export default defineConfig({
  root: fileURLToPath(new URL(".", import.meta.url)),
  plugins: [react()],
  resolve: {
    alias: { "@": fileURLToPath(new URL("../src", import.meta.url)) },
  },
  build: { outDir: "dist", emptyOutDir: true },
  server: { port: 5174 },
});
