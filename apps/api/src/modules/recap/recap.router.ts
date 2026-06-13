import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import { getRecapHandler, getRecapYearsHandler } from "./recap.handlers";

export const registerRecapRoutes = (app: Elysia) =>
  app.use(
    withAuth(new Elysia())
      .get("/api/recap/years", getRecapYearsHandler)
      .get("/api/recap", getRecapHandler)
  );
