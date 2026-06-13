import type {
  AuthenticatedContext,
  AuthenticatedParamsContext,
} from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import { AppError } from "../../common/errors/AppError";
import { parseGameId } from "../../common/utils/gameId";
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

export const getIgnoreStatusHandler = async (
  ctx: AuthenticatedContext
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const url = new URL(ctx.request.url);
  const gameId = parseGameId(url.searchParams.get("gameId"), "gameId");

  const response = await gamesService.getIgnoreStatus(auth.userId, gameId);
  return ok(response);
};

export const ignoreGameHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const payload = ctx.body as { gameId?: number | string } | null;
  const gameId = parseGameId(payload?.gameId ?? null, "gameId");

  const response = await gamesService.ignoreGame(auth.userId, gameId);
  return ok(response);
};

export const unignoreGameHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const payload = ctx.body as { gameId?: number | string } | null;
  const gameId = parseGameId(payload?.gameId ?? null, "gameId");

  const response = await gamesService.unignoreGame(auth.userId, gameId);
  return ok(response);
};

export const getRatingHandler = async (
  ctx: AuthenticatedContext
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const url = new URL(ctx.request.url);
  const gameId = parseGameId(url.searchParams.get("gameId"), "gameId");

  const response = await gamesService.getRating(auth.userId, gameId);
  return ok(response);
};

export const upsertRatingHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const payload = ctx.body as {
    gameId?: number | string;
    rating?: number;
  } | null;
  const gameId = parseGameId(payload?.gameId ?? null, "gameId");
  const rating = parseRating(payload?.rating ?? null);

  const response = await gamesService.upsertRating(auth.userId, gameId, rating);
  return ok(response);
};

export const deleteRatingHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const payload = ctx.body as { gameId?: number | string } | null;
  const gameId = parseGameId(payload?.gameId ?? null, "gameId");

  const response = await gamesService.deleteRating(auth.userId, gameId);
  return ok(response);
};

export const getGameHandler = async (
  ctx: AuthenticatedParamsContext
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const raw = ctx.params?.gameId ?? "";
  const gameId = parseGameId(raw, "gameId");
  const response = await gamesService.getGameById(auth.userId, gameId);
  return ok(response);
};

export const searchGamesHandler = async (
  ctx: AuthenticatedContext
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const url = new URL(ctx.request.url);
  const query = url.searchParams.get("query") ?? "";
  const limitRaw = url.searchParams.get("limit");
  const limit = limitRaw ? Number(limitRaw) : 10;
  if (limitRaw && (!Number.isInteger(limit) || limit <= 0)) {
    throw new AppError("limit must be a positive integer.", 400);
  }
  const response = await gamesService.searchGames(auth.userId, query, limit);
  return ok(response);
};

export const listConnectionsHandler = async (
  ctx: AuthenticatedContext
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const response = await gamesService.listConnections(auth.userId);
  return ok(response);
};

export const createConnectionHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const payload = ctx.body as {
    gameId?: number | string;
    connectedGameId?: number | string;
  } | null;
  const gameId = parseGameId(payload?.gameId ?? null, "gameId");
  const connectedGameId = parseGameId(
    payload?.connectedGameId ?? null,
    "connectedGameId"
  );

  const response = await gamesService.connectGames(
    auth.userId,
    gameId,
    connectedGameId
  );
  return ok(response);
};

export const deleteConnectionHandler = async (
  ctx: AuthenticatedContext & { body?: unknown }
): Promise<ApiResponse<unknown>> => {
  const { auth } = ctx;
  const payload = ctx.body as {
    gameId?: number | string;
    connectedGameId?: number | string;
  } | null;
  const gameId = parseGameId(payload?.gameId ?? null, "gameId");
  const connectedGameId = parseGameId(
    payload?.connectedGameId ?? null,
    "connectedGameId"
  );

  const response = await gamesService.disconnectGames(
    auth.userId,
    gameId,
    connectedGameId
  );
  return ok(response);
};
