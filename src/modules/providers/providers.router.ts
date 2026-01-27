import type { Elysia } from "elysia";

import {
  getPlayniteKeyHandler,
  listProvidersHandler,
} from "./providers.handlers";

export const registerProviderRoutes = (app: Elysia) =>
  app
    .get("/api/providers", listProvidersHandler)
    .post("/api/playnite/key", getPlayniteKeyHandler);

