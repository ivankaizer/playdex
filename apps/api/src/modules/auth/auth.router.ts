import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import { loginHandler, meHandler, refreshHandler } from "./auth.handlers";

export const registerAuthRoutes = (app: Elysia) =>
  app
    .post("/api/login", loginHandler)
    .post("/api/refresh", refreshHandler)
    .use(withAuth(new Elysia()).get("/api/me", meHandler));
