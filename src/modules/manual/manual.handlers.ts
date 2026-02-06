import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type {
  ManualEntryCreateResponseDto,
  ManualInputsResponseDto,
  ManualMappingResponseDto,
  ManualMappingSearchResponseDto,
  ManualSessionStartResponseDto,
  ManualSessionStopResponseDto,
  ApiResponse,
} from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { config } from "../../common/config";
import { parseGameId } from "../../common/utils/gameId";
import { encodeGameId } from "../../common/utils/gameHash";
import { manualService } from "./manual.service";

type ManualSessionStartBody = {
  title?: string;
};

type ManualMappingBody = {
  gameId?: number | string;
  igdbId?: string;
  title?: string;
  platformKey?: string | null;
  platformId?: number | null;
  capsuleUrl?: string | null;
};

type ManualEntryBody = {
  date?: string;
  durationMinutes?: number;
  gameId?: number | string;
  igdbId?: string;
  title?: string;
  platformKey?: string | null;
  capsuleUrl?: string | null;
};

const parseDateInput = (raw: string) => {
  const [year, month, day] = raw.split("-").map(Number);
  if (!year || !month || !day) {
    return null;
  }
  const parsed = new Date(Date.UTC(year, month - 1, day));
  if (Number.isNaN(parsed.getTime())) {
    return null;
  }
  return parsed;
};

const parsePositiveInteger = (value: unknown, field: string) => {
  const numeric =
    typeof value === "number" ? value : value ? Number(value) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric <= 0) {
    throw new AppError(`${field} must be a positive integer.`, 400);
  }
  return numeric;
};

export const listManualInputsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<ManualInputsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const inputs = await manualService.getManualInputs(auth.userId);
  return ok({ inputs });
};

export const startManualSessionHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<ManualSessionStartResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as ManualSessionStartBody | null;
  const title = payload?.title?.trim() ?? "";
  if (!title) {
    throw new AppError("Title is required.", 400);
  }

  const result = await manualService.startManualSession(auth.userId, title);
  return ok({ sessionId: result.sessionId });
};

export const stopManualSessionHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<ManualSessionStopResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const result = await manualService.stopManualSession(auth.userId);
  return ok({ endedSessionCount: result.endedSessionCount });
};

export const searchManualMappingsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<ManualMappingSearchResponseDto>> => {
  requireAuth(ctx.request);

  const url = new URL(ctx.request.url);
  const query = url.searchParams.get("query")?.trim() ?? "";
  if (!query) {
    throw new AppError("query is required.", 400);
  }

  const igdbAvailable = Boolean(
    config.igdbClientId && config.igdbClientSecret
  );

  const results = await manualService.searchManualMappingOptions(query);
  return ok({
    query,
    igdbAvailable,
    results: [...results.local, ...results.igdb],
  });
};

export const createManualEntryHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<ManualEntryCreateResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const payload = ctx.body as ManualEntryBody | null;
  const rawDate = payload?.date?.trim() ?? "";
  const startedAt = rawDate ? parseDateInput(rawDate) : null;
  if (!startedAt) {
    throw new AppError("date is required.", 400);
  }

  const durationRaw = payload?.durationMinutes;
  const durationMinutes =
    typeof durationRaw === "number" ? durationRaw : Number(durationRaw);
  if (
    !Number.isFinite(durationMinutes) ||
    durationMinutes <= 0 ||
    !Number.isInteger(durationMinutes)
  ) {
    throw new AppError("durationMinutes must be a positive integer.", 400);
  }

  const durationSeconds = durationMinutes * 60;

  const gameIdRaw = payload?.gameId;
  const hasGameId =
    typeof gameIdRaw === "number" ||
    (typeof gameIdRaw === "string" && gameIdRaw.trim().length > 0);
  const gameId = hasGameId ? parseGameId(gameIdRaw, "gameId") : null;

  if (gameId) {
    const result = await manualService.createManualEntry({
      userId: auth.userId,
      startedAt,
      durationSeconds,
      selection: { type: "local", gameId },
    });
    return ok({ sessionId: result.sessionId });
  }

  const igdbId = payload?.igdbId?.trim() ?? "";
  const title = payload?.title?.trim() ?? "";
  const platformKey = payload?.platformKey?.trim() ?? "";

  if (!igdbId || !title || !platformKey) {
    throw new AppError("igdbId, title, and platformKey are required.", 400);
  }

  const result = await manualService.createManualEntry({
    userId: auth.userId,
    startedAt,
    durationSeconds,
    selection: {
      type: "igdb",
      igdbId,
      title,
      platform: platformKey,
      coverUrl: payload?.capsuleUrl ?? null,
    },
  });

  return ok({ sessionId: result.sessionId });
};

export const mapManualInputHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
  body?: unknown;
}): Promise<ApiResponse<ManualMappingResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const inputId = parsePositiveInteger(ctx.params?.inputId, "inputId");
  const payload = ctx.body as ManualMappingBody | null;

  const gameIdRaw = payload?.gameId;
  const hasGameId =
    typeof gameIdRaw === "number" ||
    (typeof gameIdRaw === "string" && gameIdRaw.trim().length > 0);
  const gameId = hasGameId ? parseGameId(gameIdRaw, "gameId") : null;
  const igdbId = payload?.igdbId?.trim() ?? "";
  const title = payload?.title?.trim() ?? "";

  if (gameId) {
    const updated = await manualService.mapManualInput(auth.userId, inputId, {
      type: "local",
      gameId,
    });
    if (!updated) {
      throw new AppError("Manual input not found.", 404);
    }
    return ok({
      inputId: updated.id,
      mappedGameId: updated.mapped_game_id
        ? encodeGameId(Number(updated.mapped_game_id))
        : null,
    });
  }

  if (!igdbId || !title) {
    throw new AppError("igdbId and title are required.", 400);
  }

  const updated = await manualService.mapManualInput(auth.userId, inputId, {
    type: "igdb",
    igdbId,
    title,
    platform: payload?.platformKey ?? null,
    platformId:
      typeof payload?.platformId === "number" ? payload.platformId : null,
    coverUrl: payload?.capsuleUrl ?? null,
  });

  if (!updated) {
    throw new AppError("Manual input not found.", 404);
  }

  return ok({
    inputId: updated.id,
    mappedGameId: updated.mapped_game_id
      ? encodeGameId(Number(updated.mapped_game_id))
      : null,
  });
};
