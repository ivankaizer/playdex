import type { Elysia } from "elysia";

import {
  getUserSettingsHandler,
  updateUserSettingsHandler,
} from "./settings.handlers";

export const registerSettingsRoutes = (app: Elysia) =>
  app
    .get("/api/settings", getUserSettingsHandler)
    .put("/api/settings", updateUserSettingsHandler);
