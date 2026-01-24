import { Elysia } from "elysia";
import { cors } from "@elysiajs/cors";
import { swagger } from "@elysiajs/swagger";

import { registerAccountRoutes } from "../accounts/routes";
import { registerAuthRoutes } from "../auth/routes";
import { registerMetricsRoutes } from "../metrics/routes";
import { registerManualRoutes } from "../manual/routes";
import { registerGameRoutes } from "../games/routes";
import { registerPlayniteRoutes } from "../playnite/routes";
import { registerProviderRoutes } from "../providers/routes";
import { registerRootRoutes } from "../root/routes";
import type { PollingNotifier } from "../polling/polling";
import { config } from "../../config/env";

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
  registerMetricsRoutes(app);
  registerAccountRoutes(app);
  registerGameRoutes(app);
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
