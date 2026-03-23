import { cors } from "@elysiajs/cors";
import { swagger } from "@elysiajs/swagger";
import { Elysia } from "elysia";

import { config } from "../common/config";
import { registerAdminRoutes } from "../modules/admin";
import { registerAccountRoutes } from "../modules/accounts";
import { registerAuthRoutes } from "../modules/auth";
import { registerGameRoutes } from "../modules/games";
import { registerManualRoutes } from "../modules/manual";
import { registerMetricsRoutes } from "../modules/metrics";
import { registerPlayniteRoutes } from "../modules/playnite";
import type { PollingNotifier } from "../modules/polling";
import { registerProviderRoutes } from "../modules/providers";
import { registerRecapRoutes } from "../modules/recap";
import { registerRootRoutes } from "../modules/root";
import { registerSettingsRoutes } from "../modules/settings";
import { registerErrorHandling } from "./http/errorHandler";

type AppOptions = {
  notifier?: PollingNotifier;
};

export const createApp = (options: AppOptions = {}) => {
  const app = new Elysia();

  registerErrorHandling(app);

  if (config.corsOrigins.length > 0) {
    app.use(
      cors({
        origin: config.corsOrigins,
      })
    );
  } else {
    console.warn(
      "CORS_ORIGINS not configured; cross-origin requests are disabled."
    );
  }

  registerRootRoutes(app);
  registerAuthRoutes(app);
  registerAdminRoutes(app);
  registerMetricsRoutes(app);
  registerAccountRoutes(app);
  registerGameRoutes(app);
  registerSettingsRoutes(app);
  registerPlayniteRoutes(app, options.notifier);
  registerProviderRoutes(app);
  registerManualRoutes(app);
  registerRecapRoutes(app);

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
