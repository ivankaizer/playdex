import type { Elysia } from "elysia";

import { getMetrics } from "../../shared/observability";
import type { MetricsResponseDto } from "./types";
import { ok } from "./utils";

export const registerMetricsRoutes = (app: Elysia) =>
  app.get("/api/metrics", () => {
    const response: MetricsResponseDto = getMetrics();
    return ok(response);
  });
