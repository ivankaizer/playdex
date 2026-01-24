import type { Elysia } from "elysia";

import {
  ignoreGameForUser,
  isGameIgnoredForUser,
  unignoreGameForUser,
} from "../../shared/db/queries";
import type { GameIgnoreResponseDto } from "../../shared/http/types";
import {
  fail,
  ok,
  requireAuth,
  withApiErrorLogging,
} from "../../shared/http/utils";

type IgnoreGameBody = {
  gameId?: number;
};

const parseGameId = (raw: string | number | null) => {
  const numeric =
    typeof raw === "number" ? raw : raw ? Number(raw) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric <= 0) {
    return { ok: false as const, message: "gameId must be a positive integer." };
  }
  return { ok: true as const, gameId: numeric };
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
    );
