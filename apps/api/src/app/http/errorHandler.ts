import type { Elysia } from "elysia";

import { isAppError } from "../../common/errors/AppError";
import { getErrorMessage, logEvent } from "../../common/observability";
import { fail } from "./responses";

export const registerErrorHandling = (app: Elysia) =>
  app.onError(({ error, code, request, set }) => {
    if (code === "NOT_FOUND") {
      set.status = 404;
      return fail("Route not found.");
    }

    if (isAppError(error)) {
      const level = error.statusCode >= 500 ? "error" : "warn";
      logEvent(level, "http.error", {
        path: new URL(request.url).pathname,
        method: request.method,
        message: error.message,
        code: error.code,
      });
      set.status = error.statusCode;
      return fail(error.message);
    }

    logEvent("error", "http.unhandled", {
      path: new URL(request.url).pathname,
      method: request.method,
      message: getErrorMessage(error),
    });
    set.status = 500;
    return fail("Internal server error.");
  });
