import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import { AppError } from "../../common/errors/AppError";
import type { ApiResponse } from "../../app/http/types";
import { gamesService } from "./games.service";

const parsePositiveInteger = (value: unknown, field: string) => {
  const numeric =
    typeof value === "number" ? value : value ? Number(value) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric <= 0) {
    throw new AppError(`${field} must be a positive integer.`, 400);
  }
  return numeric;
};

const parseRating = (value: unknown) => parsePositiveInteger(value, "rating");

export const getIgnoreStatusHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  const url = new URL(ctx.request.url);
  const gameId = parsePositiveInteger(url.searchParams.get("gameId"), "gameId");

  const response = await gamesService.getIgnoreStatus(auth.userId, gameId);
  return ok(response);
};

export const ignoreGameHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as { gameId?: number } | null;
  const gameId = parsePositiveInteger(payload?.gameId ?? null, "gameId");

  const response = await gamesService.ignoreGame(auth.userId, gameId);
  return ok(response);
};

export const unignoreGameHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as { gameId?: number } | null;
  const gameId = parsePositiveInteger(payload?.gameId ?? null, "gameId");

  const response = await gamesService.unignoreGame(auth.userId, gameId);
  return ok(response);
};

export const getRatingHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  const url = new URL(ctx.request.url);
  const gameId = parsePositiveInteger(url.searchParams.get("gameId"), "gameId");

  const response = await gamesService.getRating(auth.userId, gameId);
  return ok(response);
};

export const upsertRatingHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as { gameId?: number; rating?: number } | null;
  const gameId = parsePositiveInteger(payload?.gameId ?? null, "gameId");
  const rating = parseRating(payload?.rating ?? null);

  const response = await gamesService.upsertRating(auth.userId, gameId, rating);
  return ok(response);
};

export const deleteRatingHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<unknown>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as { gameId?: number } | null;
  const gameId = parsePositiveInteger(payload?.gameId ?? null, "gameId");

  const response = await gamesService.deleteRating(auth.userId, gameId);
  return ok(response);
};

