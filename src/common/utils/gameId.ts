import { AppError } from "../errors/AppError";
import { decodeGameHash } from "./gameHash";

export const parseGameId = (value: unknown, field = "gameId") => {
  if (typeof value === "number") {
    if (Number.isInteger(value) && value > 0) {
      return value;
    }
  }

  const raw = typeof value === "string" ? value.trim() : "";
  if (!raw) {
    throw new AppError(`${field} is required.`, 400);
  }

  const decoded = decodeGameHash(raw);
  if (decoded) {
    return decoded;
  }

  const numeric = Number(raw);
  if (Number.isInteger(numeric) && numeric > 0) {
    return numeric;
  }

  throw new AppError(`${field} must be a valid game id.`, 400);
};
