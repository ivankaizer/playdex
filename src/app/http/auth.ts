import jwt from "jsonwebtoken";

import { AppError } from "../../common/errors/AppError";

export type AuthContext = { userId: number; email: string };

export const getJwtSecret = () => {
  const jwtSecret = Bun.env.JWT_SECRET;
  if (!jwtSecret?.trim()) {
    throw new AppError("JWT secret is not configured.", 500);
  }
  return jwtSecret;
};

const parseBearerToken = (header: string | null) => {
  if (!header?.startsWith("Bearer ")) {
    return "";
  }
  return header.slice("Bearer ".length).trim();
};

export const requireAuth = (request: Request): AuthContext => {
  const token = parseBearerToken(request.headers.get("authorization"));
  if (!token) {
    throw new AppError("Missing token.", 401);
  }

  try {
    const decoded = jwt.verify(token, getJwtSecret()) as {
      sub: number;
      email: string;
      type?: string;
    };

    if (decoded.type !== "access") {
      throw new AppError("Invalid token.", 401);
    }

    return { userId: decoded.sub, email: decoded.email };
  } catch (error) {
    if (error instanceof AppError) {
      throw error;
    }
    throw new AppError("Invalid token.", 401, { cause: error });
  }
};
