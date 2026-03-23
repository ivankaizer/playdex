import type { Elysia } from "elysia";

import {
  listRecentSessionsHandler,
  listSessionsHandler,
  listPlatformStatusesHandler,
  updatePlatformStatusHandler,
  listCredentialsHandler,
  updateCredentialHandler,
  moveSessionsHandler,
  searchGamesHandler,
  updateGameHandler,
} from "./admin.handlers";

export const registerAdminRoutes = (app: Elysia) =>
  app
    .get("/api/admin/sessions/recent", listRecentSessionsHandler)
    .get("/api/admin/sessions", listSessionsHandler)
    .put("/api/admin/sessions/move", moveSessionsHandler)
    .get("/api/admin/providers/platforms", listPlatformStatusesHandler)
    .put("/api/admin/providers/platforms/status", updatePlatformStatusHandler)
    .get("/api/admin/credentials", listCredentialsHandler)
    .put("/api/admin/credentials/:credentialId", updateCredentialHandler)
    .get("/api/admin/games/search", searchGamesHandler)
    .put("/api/admin/games", updateGameHandler)
    .put("/api/admin/games/:gameId", updateGameHandler);
