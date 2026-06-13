import { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import { metricsHandler } from "./metrics.handlers";

export const registerMetricsRoutes = (app: Elysia) =>
  app.use(withAuth(new Elysia()).get("/api/metrics", metricsHandler));
