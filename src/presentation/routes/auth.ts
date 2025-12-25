import type { Elysia } from "elysia";
import { compare } from "bcryptjs";
import jwt from "jsonwebtoken";

import { loadUserByEmail } from "../../infrastructure/db/queries";
import { fail, ok, withApiErrorLogging } from "./utils";

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
      const jwtSecret = Bun.env.JWT_SECRET;
      if (!jwtSecret?.trim()) {
        set.status = 500;
        return fail("JWT secret is not configured.");
      }

      const authHeader = request.headers.get("authorization") ?? "";
      const token = authHeader.startsWith("Bearer ")
        ? authHeader.slice("Bearer ".length).trim()
        : "";

      if (!token) {
        set.status = 401;
        return fail("Missing token.");
      }

      try {
        const decoded = jwt.verify(token, jwtSecret) as {
          sub: number;
          email: string;
        };

        return ok({ userId: decoded.sub, email: decoded.email });
      } catch (error) {
        set.status = 401;
        return fail("Invalid token.");
      }
    })
  );
