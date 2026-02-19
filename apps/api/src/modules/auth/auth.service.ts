import { compare } from "bcryptjs";
import jwt from "jsonwebtoken";

import { getJwtSecret } from "../../app/http/auth";
import { config } from "../../common/config";
import { AppError } from "../../common/errors/AppError";
import { authRepository } from "./auth.repository";

const REFRESH_TOKEN_TTL = "30d";

type TokenPayload = {
  sub: number;
  email: string;
  type: "access" | "refresh";
};

const signToken = (
  payload: TokenPayload,
  expiresIn: jwt.SignOptions["expiresIn"]
) => jwt.sign(payload, getJwtSecret(), { expiresIn });

export const authService = {
  login: async (email: string, password: string) => {
    const user = await authRepository.loadUserByEmail(email);

    if (!user || !user.password) {
      throw new AppError("Invalid credentials.", 401);
    }

    const isValid = await compare(password, user.password);

    if (!isValid) {
      throw new AppError("Invalid credentials.", 401);
    }

    const accessToken = signToken(
      { sub: user.id, email: user.email, type: "access" },
      config.accessTokenTtl as jwt.SignOptions["expiresIn"]
    );
    const refreshToken = signToken(
      { sub: user.id, email: user.email, type: "refresh" },
      REFRESH_TOKEN_TTL
    );

    return {
      userId: user.id,
      email: user.email,
      accessToken,
      refreshToken,
    };
  },

  refresh: async (token: string) => {
    try {
      const decoded = jwt.verify(
        token,
        getJwtSecret()
      ) as unknown as TokenPayload;
      if (decoded.type !== "refresh") {
        throw new AppError("Invalid token.", 401);
      }

      const accessToken = signToken(
        { sub: decoded.sub, email: decoded.email, type: "access" },
        config.accessTokenTtl as jwt.SignOptions["expiresIn"]
      );
      const nextRefreshToken = signToken(
        { sub: decoded.sub, email: decoded.email, type: "refresh" },
        REFRESH_TOKEN_TTL
      );

      return { accessToken, refreshToken: nextRefreshToken };
    } catch (error) {
      if (error instanceof AppError) {
        throw error;
      }
      throw new AppError("Invalid token.", 401, { cause: error });
    }
  },
};
