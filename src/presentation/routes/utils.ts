import jwt from "jsonwebtoken";

import { getErrorMessage, logEvent } from "../../shared/observability";
import type { ApiErrorResponse, ApiResponse } from "./types";

type AccountIdParseResult =
  | { ok: true; accountId: number }
  | { ok: false; response: ApiResponse<never> };

export const ok = <T>(data: T): ApiResponse<T> => ({ data, error: null });

export const fail = (message: string): ApiResponse<never> => ({
  data: null,
  error: { error: message },
});

export const withApiErrorLogging =
  <
    T extends {
      request: Request;
      set: { status?: number | string };
      params?: Record<string, string>;
      body?: unknown;
    },
    R,
  >(
    handler: (ctx: T) => Promise<ApiResponse<R>>
  ) =>
  async (ctx: T): Promise<ApiResponse<R>> => {
    try {
      return await handler(ctx);
    } catch (error) {
      logEvent("error", "api.error", {
        method: ctx.request.method,
        path: new URL(ctx.request.url).pathname,
        message: getErrorMessage(error),
      });
      ctx.set.status = 500;
      return fail("Internal server error.");
    }
  };

export const parseAccountIdParam = (
  params: Record<string, string> | undefined
): AccountIdParseResult => {
  const raw = params?.accountId?.trim() ?? "";
  const accountId = Number(raw);
  if (!raw || !Number.isInteger(accountId)) {
    return {
      ok: false,
      response: fail("accountId must be a number."),
    };
  }

  return { ok: true, accountId };
};

type AuthResult =
  | { ok: true; userId: number; email: string }
  | { ok: false; status: number; response: ApiResponse<never> };

export const requireAuth = (request: Request): AuthResult => {
  const jwtSecret = Bun.env.JWT_SECRET;
  if (!jwtSecret?.trim()) {
    return {
      ok: false,
      status: 500,
      response: fail("JWT secret is not configured."),
    };
  }

  const authHeader = request.headers.get("authorization") ?? "";
  const token = authHeader.startsWith("Bearer ")
    ? authHeader.slice("Bearer ".length).trim()
    : "";

  if (!token) {
    return { ok: false, status: 401, response: fail("Missing token.") };
  }

  try {
    const decoded = jwt.verify(token, jwtSecret) as {
      sub: number;
      email: string;
      type?: string;
    };

    if (decoded.type !== "access") {
      return { ok: false, status: 401, response: fail("Invalid token.") };
    }

    return { ok: true, userId: decoded.sub, email: decoded.email };
  } catch (error) {
    return { ok: false, status: 401, response: fail("Invalid token.") };
  }
};
