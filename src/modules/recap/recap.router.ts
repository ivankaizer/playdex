import type { Elysia } from "elysia";

import { getRecapHandler, getRecapYearsHandler } from "./recap.handlers";

export const registerRecapRoutes = (app: Elysia) =>
  app
    .get("/api/recap/years", getRecapYearsHandler)
    .get("/api/recap", getRecapHandler);
