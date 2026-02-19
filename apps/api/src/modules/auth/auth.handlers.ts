import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type { ApiResponse } from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { authService } from "./auth.service";

export const loginHandler = async (ctx: {
  body?: unknown;
}): Promise<ApiResponse<unknown>> => {
  const payload = ctx.body as { email?: string; password?: string } | null;
  const email = payload?.email?.trim() ?? "";
  const password = payload?.password ?? "";

  if (!email || !password) {
    throw new AppError("Email and password are required.", 400);
  }

  const result = await authService.login(email, password);
  return ok(result);
};

export const refreshHandler = async (ctx: {
  body?: unknown;
}): Promise<ApiResponse<unknown>> => {
  const payload = ctx.body as { refreshToken?: string } | null;
  const refreshToken = payload?.refreshToken?.trim() ?? "";

  if (!refreshToken) {
    throw new AppError("Refresh token is required.", 400);
  }

  const result = await authService.refresh(refreshToken);
  return ok(result);
};

export const meHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  return ok({ userId: auth.userId, email: auth.email });
};
