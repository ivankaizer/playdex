import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import {
  getPlayniteKeyHandler,
  listProvidersHandler,
  updateProviderStatusHandler,
} from "./providers.handlers";

export const registerProviderRoutes = (app: Elysia) =>
  app.use(
    withAuth(new Elysia())
      .get("/api/providers", listProvidersHandler)
      .put("/api/providers/status", updateProviderStatusHandler)
      .post("/api/playnite/key", getPlayniteKeyHandler)
  );
