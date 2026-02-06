import type { Elysia } from "elysia";

import {
  getPlayniteKeyHandler,
  listProvidersHandler,
  updateProviderStatusHandler,
} from "./providers.handlers";

export const registerProviderRoutes = (app: Elysia) =>
  app
    .get("/api/providers", listProvidersHandler)
    .put("/api/providers/status", updateProviderStatusHandler)
    .post("/api/playnite/key", getPlayniteKeyHandler);
