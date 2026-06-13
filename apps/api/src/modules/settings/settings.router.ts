import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import {
  getUserSettingsHandler,
  updateUserSettingsHandler,
} from "./settings.handlers";

export const registerSettingsRoutes = (app: Elysia) =>
  app.use(
    withAuth(new Elysia())
      .get("/api/settings", getUserSettingsHandler)
      .put("/api/settings", updateUserSettingsHandler)
  );
