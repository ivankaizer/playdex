import type { Elysia } from "elysia";

import { ok } from "../../shared/http/utils";

export const registerRootRoutes = (app: Elysia) =>
  app.get("/api", () => ok("Ok"));
