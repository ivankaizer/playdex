import type { Elysia } from "elysia";

import {
  createConnectionHandler,
  deleteConnectionHandler,
  deleteRatingHandler,
  getGameHandler,
  getIgnoreStatusHandler,
  getRatingHandler,
  ignoreGameHandler,
  listConnectionsHandler,
  searchGamesHandler,
  unignoreGameHandler,
  upsertRatingHandler,
} from "./games.handlers";

export const registerGameRoutes = (app: Elysia) =>
  app
    .get("/api/games/search", searchGamesHandler)
    .get("/api/games/connections", listConnectionsHandler)
    .post("/api/games/connections", createConnectionHandler)
    .delete("/api/games/connections", deleteConnectionHandler)
    .get("/api/games/ignored", getIgnoreStatusHandler)
    .post("/api/games/ignored", ignoreGameHandler)
    .delete("/api/games/ignored", unignoreGameHandler)
    .get("/api/games/rating", getRatingHandler)
    .post("/api/games/rating", upsertRatingHandler)
    .delete("/api/games/rating", deleteRatingHandler)
    .get("/api/games/:gameId", getGameHandler);
