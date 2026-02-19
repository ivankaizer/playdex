import type { Elysia } from "elysia";

import { metricsHandler } from "./metrics.handlers";

export const registerMetricsRoutes = (app: Elysia) =>
  app.get("/api/metrics", metricsHandler);

