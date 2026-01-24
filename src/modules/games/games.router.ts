import type { Elysia } from "elysia";

import {
  deleteRatingHandler,
  getIgnoreStatusHandler,
  getRatingHandler,
  ignoreGameHandler,
  unignoreGameHandler,
  upsertRatingHandler,
} from "./games.handlers";

export const registerGameRoutes = (app: Elysia) =>
  app
    .get("/api/games/ignored", getIgnoreStatusHandler)
    .post("/api/games/ignored", ignoreGameHandler)
    .delete("/api/games/ignored", unignoreGameHandler)
    .get("/api/games/rating", getRatingHandler)
    .post("/api/games/rating", upsertRatingHandler)
    .delete("/api/games/rating", deleteRatingHandler);

