import { Elysia } from "elysia";
import { cors } from "@elysiajs/cors";

import { registerAccountRoutes } from "./routes/accounts";
import { registerAuthRoutes } from "./routes/auth";
import { registerHealthRoutes } from "./routes/health";
import { registerMetricsRoutes } from "./routes/metrics";
import { registerManualRoutes } from "./routes/manual";
import { registerPlayniteRoutes } from "./routes/playnite";
import { registerProviderRoutes } from "./routes/providers";
import { registerRootRoutes } from "./routes/root";
import type { PollingNotifier } from "../application/polling";
import { config } from "../config/env";

type ServerOptions = {
  notifier?: PollingNotifier;
};

export const createServer = (options: ServerOptions = {}) => {
  const app = new Elysia();

  app.use(
    cors({
      origin: config.corsOrigins.length > 0 ? config.corsOrigins : true,
    })
  );

  registerRootRoutes(app);
  registerAuthRoutes(app);
  registerHealthRoutes(app);
  registerMetricsRoutes(app);
  registerAccountRoutes(app);
  registerPlayniteRoutes(app, options.notifier);
  registerProviderRoutes(app);
  registerManualRoutes(app);

  return app;
};
