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
