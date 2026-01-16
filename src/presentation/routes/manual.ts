import type { Elysia } from "elysia";

import {
  getManualInputs,
  mapManualInput,
  searchManualMappingOptions,
  startManualSession,
  stopManualSession,
} from "../../application/manual";
import type {
  ManualInputsResponseDto,
  ManualMappingResponseDto,
  ManualMappingSearchResponseDto,
  ManualSessionStartResponseDto,
  ManualSessionStopResponseDto,
} from "./types";
import { config } from "../../config/env";
import { fail, ok, requireAuth, withApiErrorLogging } from "./utils";

type ManualSessionStartBody = {
  title?: string;
};

type ManualMappingBody = {
  gameId?: number;
  igdbId?: string;
  title?: string;
  platformKey?: string | null;
  platformId?: number | null;
  capsuleUrl?: string | null;
};

export const registerManualRoutes = (app: Elysia) =>
  app
    .get(
      "/api/manual/inputs",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const inputs = await getManualInputs(auth.userId);
        const response: ManualInputsResponseDto = { inputs };
        return ok(response);
      })
    )
    .post(
      "/api/manual/sessions/start",
      withApiErrorLogging(async ({ request, body, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const payload = body as ManualSessionStartBody | null;
        const title = payload?.title?.trim() ?? "";
        if (!title) {
          set.status = 400;
          return fail("title is required.");
        }

        const result = await startManualSession(auth.userId, title);
        const response: ManualSessionStartResponseDto = {
          sessionId: result.sessionId,
        };
        return ok(response);
      })
    )
    .post(
      "/api/manual/sessions/stop",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const result = await stopManualSession(auth.userId);
        const response: ManualSessionStopResponseDto = {
          endedSessionCount: result.endedSessionCount,
        };
        return ok(response);
      })
    )
    .get(
      "/api/manual/search",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const url = new URL(request.url);
        const query = url.searchParams.get("query")?.trim() ?? "";
        if (!query) {
          set.status = 400;
          return fail("query is required.");
        }

        const igdbAvailable = Boolean(
          config.igdbClientId && config.igdbClientSecret
        );

        const results = await searchManualMappingOptions(query);
        const response: ManualMappingSearchResponseDto = {
          query,
          igdbAvailable,
          results: [...results.local, ...results.igdb],
        };
        return ok(response);
      })
    )
    .post(
      "/api/manual/inputs/:inputId/map",
      withApiErrorLogging(async ({ request, body, params = {}, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const inputId = Number(params.inputId);
        if (!Number.isInteger(inputId)) {
          set.status = 400;
          return fail("inputId must be a number.");
        }

        const payload = body as ManualMappingBody | null;
        const gameIdRaw = payload?.gameId;
        const gameId =
          typeof gameIdRaw === "number" ? gameIdRaw : Number(gameIdRaw);
        const igdbId = payload?.igdbId?.trim() ?? "";
        const title = payload?.title?.trim() ?? "";

        if (Number.isInteger(gameId) && gameId > 0) {
          const updated = await mapManualInput(auth.userId, inputId, {
            type: "local",
            gameId,
          });
          if (!updated) {
            set.status = 404;
            return fail("Manual input not found.");
          }
          const response: ManualMappingResponseDto = {
            inputId: updated.id,
            mappedGameId: updated.mapped_game_id ?? null,
          };
          return ok(response);
        }

        if (!igdbId || !title) {
          set.status = 400;
          return fail("igdbId and title are required.");
        }

        const updated = await mapManualInput(auth.userId, inputId, {
          type: "igdb",
          igdbId,
          title,
          platform: payload?.platformKey ?? null,
          platformId:
            typeof payload?.platformId === "number"
              ? payload.platformId
              : null,
          coverUrl: payload?.capsuleUrl ?? null,
        });

        if (!updated) {
          set.status = 404;
          return fail("Manual input not found.");
        }

        const response: ManualMappingResponseDto = {
          inputId: updated.id,
          mappedGameId: updated.mapped_game_id ?? null,
        };
        return ok(response);
      })
    );
