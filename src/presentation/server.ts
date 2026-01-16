import { Elysia } from "elysia";
import { cors } from "@elysiajs/cors";
import { swagger } from "@elysiajs/swagger";

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

  app.use(
    swagger({
      scalarConfig: {
        servers: [
          {
            url: "https://app.playdex.orb.local",
            description: "Local",
          },
          {
            url: "https://playdex.ejwan.dev/",
            description: "Production",
          },
        ],
        defaultHttpClient: {
          targetKey: "shell",
          clientKey: "bash",
        },
      },
      documentation: {
        info: {
          title: "Playdex API",
          version: "1.0.0",
        },
        components: {
          securitySchemes: {
            bearerAuth: {
              type: "http",
              scheme: "bearer",
              bearerFormat: "JWT",
            },
          },
        },
        security: [{ bearerAuth: [] }],
      },
    })
  );

  return app;
};
