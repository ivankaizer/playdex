import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import {
  deleteUserSessionHandler,
  getAccountCurrentSessionHandler,
  getAccountStatsHandler,
  getUserCurrentSessionHandler,
  getUserStatsHandler,
  listAccountSessionsHandler,
  listAccountSessionsTodayHandler,
  listAccountsHandler,
  listUserSessionsHandler,
  listUserSessionsTodayHandler,
} from "./accounts.handlers";

export const registerAccountRoutes = (app: Elysia) =>
  app.use(
    withAuth(new Elysia())
      .get("/api/accounts", listAccountsHandler)
      .get("/api/sessions", listUserSessionsHandler)
      .delete("/api/sessions/:sessionId", deleteUserSessionHandler)
      .get("/api/sessions/today", listUserSessionsTodayHandler)
      .get("/api/sessions/current", getUserCurrentSessionHandler)
      .get("/api/stats", getUserStatsHandler)
      .get("/api/accounts/:accountId/sessions", listAccountSessionsHandler)
      .get(
        "/api/accounts/:accountId/sessions/today",
        listAccountSessionsTodayHandler
      )
      .get(
        "/api/accounts/:accountId/sessions/current",
        getAccountCurrentSessionHandler
      )
      .get("/api/accounts/:accountId/stats", getAccountStatsHandler)
  );
