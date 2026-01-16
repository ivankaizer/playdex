import type { Elysia } from "elysia";
import { compare } from "bcryptjs";
import jwt from "jsonwebtoken";

import { loadUserByEmail } from "../../infrastructure/db/queries";
import { config } from "../../config/env";
import { fail, ok, requireAuth, withApiErrorLogging } from "./utils";

const ACCESS_TOKEN_TTL = config.accessTokenTtl;
const REFRESH_TOKEN_TTL = "30d";

type TokenPayload = {
  sub: number;
  email: string;
  type: "access" | "refresh";
};

const signToken = (
  payload: TokenPayload,
  jwtSecret: string,
  expiresIn: string
) => jwt.sign(payload, jwtSecret, { expiresIn });

export const registerAuthRoutes = (app: Elysia) =>
  app
    .post(
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

        const accessToken = signToken(
          { sub: user.id, email: user.email, type: "access" },
          jwtSecret,
          ACCESS_TOKEN_TTL
        );
        const refreshToken = signToken(
          { sub: user.id, email: user.email, type: "refresh" },
          jwtSecret,
          REFRESH_TOKEN_TTL
        );

        return ok({
          userId: user.id,
          email: user.email,
          accessToken,
          refreshToken,
        });
      })
    )
    .post(
      "/api/refresh",
      withApiErrorLogging(async ({ body, set }) => {
        const jwtSecret = Bun.env.JWT_SECRET;
        if (!jwtSecret?.trim()) {
          set.status = 500;
          return fail("JWT secret is not configured.");
        }

        const payload = body as { refreshToken?: string } | null;
        const refreshToken = payload?.refreshToken?.trim() ?? "";

        if (!refreshToken) {
          set.status = 400;
          return fail("Refresh token is required.");
        }

        try {
          const decoded = jwt.verify(refreshToken, jwtSecret) as TokenPayload;
          if (decoded.type !== "refresh") {
            set.status = 401;
            return fail("Invalid token.");
          }

          const accessToken = signToken(
            { sub: decoded.sub, email: decoded.email, type: "access" },
            jwtSecret,
            ACCESS_TOKEN_TTL
          );
          const nextRefreshToken = signToken(
            { sub: decoded.sub, email: decoded.email, type: "refresh" },
            jwtSecret,
            REFRESH_TOKEN_TTL
          );

          return ok({ accessToken, refreshToken: nextRefreshToken });
        } catch {
          set.status = 401;
          return fail("Invalid token.");
        }
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
