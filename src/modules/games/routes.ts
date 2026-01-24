import type { Elysia } from "elysia";

import {
  ignoreGameForUser,
  isGameIgnoredForUser,
  unignoreGameForUser,
  deleteGameRatingForUser,
  loadGameRatingForUser,
  upsertGameRatingForUser,
} from "../../shared/db/queries";
import type {
  GameIgnoreResponseDto,
  GameRatingResponseDto,
} from "../../shared/http/types";
import {
  fail,
  ok,
  requireAuth,
  withApiErrorLogging,
} from "../../shared/http/utils";

type IgnoreGameBody = {
  gameId?: number;
};

type RateGameBody = {
  gameId?: number;
  rating?: number;
};

const parseGameId = (raw: string | number | null) => {
  const numeric =
    typeof raw === "number" ? raw : raw ? Number(raw) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric <= 0) {
    return { ok: false as const, message: "gameId must be a positive integer." };
  }
  return { ok: true as const, gameId: numeric };
};

const parseRating = (raw: string | number | null) => {
  const numeric =
    typeof raw === "number" ? raw : raw ? Number(raw) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric < 1 || numeric > 5) {
    return {
      ok: false as const,
      message: "rating must be an integer between 1 and 5.",
    };
  }
  return { ok: true as const, rating: numeric };
};

export const registerGameRoutes = (app: Elysia) =>
  app
    .get(
      "/api/games/ignored",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const url = new URL(request.url);
        const parsed = parseGameId(url.searchParams.get("gameId"));
        if (!parsed.ok) {
          set.status = 400;
          return fail(parsed.message);
        }

        const ignored = await isGameIgnoredForUser(
          auth.userId,
          parsed.gameId
        );
        const response: GameIgnoreResponseDto = {
          gameId: parsed.gameId,
          ignored,
        };
        return ok(response);
      })
    )
    .post(
      "/api/games/ignored",
      withApiErrorLogging(async ({ request, body, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const payload = body as IgnoreGameBody | null;
        const parsed = parseGameId(payload?.gameId ?? null);
        if (!parsed.ok) {
          set.status = 400;
          return fail(parsed.message);
        }

        await ignoreGameForUser(auth.userId, parsed.gameId);
        const response: GameIgnoreResponseDto = {
          gameId: parsed.gameId,
          ignored: true,
        };
        return ok(response);
      })
    )
    .delete(
      "/api/games/ignored",
      withApiErrorLogging(async ({ request, body, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const payload = body as IgnoreGameBody | null;
        const parsed = parseGameId(payload?.gameId ?? null);
        if (!parsed.ok) {
          set.status = 400;
          return fail(parsed.message);
        }

        await unignoreGameForUser(auth.userId, parsed.gameId);
        const response: GameIgnoreResponseDto = {
          gameId: parsed.gameId,
          ignored: false,
        };
        return ok(response);
      })
    )
    .get(
      "/api/games/rating",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const url = new URL(request.url);
        const parsed = parseGameId(url.searchParams.get("gameId"));
        if (!parsed.ok) {
          set.status = 400;
          return fail(parsed.message);
        }

        const rating = await loadGameRatingForUser(auth.userId, parsed.gameId);
        const response: GameRatingResponseDto = {
          gameId: parsed.gameId,
          rating: rating?.rating ?? null,
        };
        return ok(response);
      })
    )
    .post(
      "/api/games/rating",
      withApiErrorLogging(async ({ request, body, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const payload = body as RateGameBody | null;
        const parsedGameId = parseGameId(payload?.gameId ?? null);
        if (!parsedGameId.ok) {
          set.status = 400;
          return fail(parsedGameId.message);
        }

        const parsedRating = parseRating(payload?.rating ?? null);
        if (!parsedRating.ok) {
          set.status = 400;
          return fail(parsedRating.message);
        }

        const rating = await upsertGameRatingForUser(
          auth.userId,
          parsedGameId.gameId,
          parsedRating.rating
        );

        const response: GameRatingResponseDto = {
          gameId: parsedGameId.gameId,
          rating: rating?.rating ?? parsedRating.rating,
        };
        return ok(response);
      })
    )
    .delete(
      "/api/games/rating",
      withApiErrorLogging(async ({ request, body, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const payload = body as RateGameBody | null;
        const parsedGameId = parseGameId(payload?.gameId ?? null);
        if (!parsedGameId.ok) {
          set.status = 400;
          return fail(parsedGameId.message);
        }

        await deleteGameRatingForUser(auth.userId, parsedGameId.gameId);
        const response: GameRatingResponseDto = {
          gameId: parsedGameId.gameId,
          rating: null,
        };
        return ok(response);
      })
    );
