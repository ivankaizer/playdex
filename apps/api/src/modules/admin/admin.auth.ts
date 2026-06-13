import type { Elysia } from "elysia";

import { withAuth } from "../../app/http/auth";
import { AppError } from "../../common/errors/AppError";
import { authRepository } from "../auth/auth.repository";

export const withAdmin = (app: Elysia) =>
  withAuth(app).derive(async ({ auth }) => {
    const user = await authRepository.loadUserById(auth.userId);
    if (!user?.is_admin) {
      throw new AppError("Admin access required.", 403);
    }

    return { admin: auth };
  });
