import { Elysia } from "elysia";

import { registerAccountRoutes } from "./routes/accounts";
import { registerHealthRoutes } from "./routes/health";
import { registerMetricsRoutes } from "./routes/metrics";
import { registerRootRoutes } from "./routes/root";

export const createServer = () => {
  const app = new Elysia();

  registerRootRoutes(app);
  registerHealthRoutes(app);
  registerMetricsRoutes(app);
  registerAccountRoutes(app);

  return app;
};
