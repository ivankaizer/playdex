import { Elysia } from "elysia";

import { registerAccountRoutes } from "./routes/accounts";
import { registerAuthRoutes } from "./routes/auth";
import { registerHealthRoutes } from "./routes/health";
import { registerMetricsRoutes } from "./routes/metrics";
import { registerPlayniteRoutes } from "./routes/playnite";
import { registerRootRoutes } from "./routes/root";
import type { PollingNotifier } from "../application/polling";

type ServerOptions = {
  notifier?: PollingNotifier;
};

export const createServer = (options: ServerOptions = {}) => {
  const app = new Elysia();

  registerRootRoutes(app);
  registerAuthRoutes(app);
  registerHealthRoutes(app);
  registerMetricsRoutes(app);
  registerAccountRoutes(app);
  registerPlayniteRoutes(app, options.notifier);

  return app;
};
