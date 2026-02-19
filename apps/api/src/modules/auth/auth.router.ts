import type { Elysia } from "elysia";

import { loginHandler, meHandler, refreshHandler } from "./auth.handlers";

export const registerAuthRoutes = (app: Elysia) =>
  app
    .post("/api/login", loginHandler)
    .post("/api/refresh", refreshHandler)
    .get("/api/me", meHandler);

