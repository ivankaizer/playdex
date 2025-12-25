import type { Elysia } from "elysia";

import { checkDatabaseHealth } from "../../application/accounts";
import type { HealthResponseDto } from "./types";
import { ok, withApiErrorLogging } from "./utils";

export const registerHealthRoutes = (app: Elysia) =>
  app.get(
    "/api/db/health",
    withApiErrorLogging(async () => {
      await checkDatabaseHealth();
      const response: HealthResponseDto = { ok: true };
      return ok(response);
    })
  );
