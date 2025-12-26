import type { Elysia } from "elysia";
import { compare } from "bcryptjs";
import jwt from "jsonwebtoken";

import { loadUserByEmail } from "../../infrastructure/db/queries";
import { fail, ok, requireAuth, withApiErrorLogging } from "./utils";

export const registerAuthRoutes = (app: Elysia) =>
  app.post(
    "/api/login",
    withApiErrorLogging(async ({ body, set }) => {
      const jwtSecret = Bun.env.JWT_SECRET;
      if (!jwtSecret?.trim()) {
        set.status = 500;
        return fail("JWT secret is not configured.");
      }

      const payload = body as { email?: string; password?: string } | null;
      const email = payload?.email?.trim() ?? "";
      const password = payload?.password ?? "";

      if (!email || !password) {
        set.status = 400;
        return fail("Email and password are required.");
      }

      const user = await loadUserByEmail(email);

      if (!user || !user.password) {
        set.status = 401;
        return fail("Invalid credentials.");
      }

      const isValid = await compare(password, user.password);

      if (!isValid) {
        set.status = 401;
        return fail("Invalid credentials.");
      }

      const token = jwt.sign(
        { sub: user.id, email: user.email },
        jwtSecret,
        { expiresIn: "7d" }
      );

      return ok({ userId: user.id, email: user.email, token });
    })
  )
  .get(
    "/api/me",
    withApiErrorLogging(async ({ request, set }) => {
      const auth = requireAuth(request);
      if (!auth.ok) {
        set.status = auth.status;
        return auth.response;
      }

      return ok({ userId: auth.userId, email: auth.email });
    })
  );
