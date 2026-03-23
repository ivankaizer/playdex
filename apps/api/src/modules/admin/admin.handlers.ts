import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type {
  AdminCredentialUpdateResponseDto,
  AdminCredentialsResponseDto,
  AdminGameSearchResponseDto,
  AdminGameUpdateResponseDto,
  AdminPlatformStatusesResponseDto,
  AdminPlatformStatusUpdateResponseDto,
  AdminRecentSessionsResponseDto,
  AdminSessionMoveResponseDto,
  AdminSessionsListResponseDto,
  ApiResponse,
} from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { parseGameId } from "../../common/utils/gameId";
import { authRepository } from "../auth/auth.repository";
import { adminService } from "./admin.service";

const parsePositiveInteger = (value: unknown, field: string) => {
  const numeric =
    typeof value === "number" ? value : value ? Number(value) : Number.NaN;
  if (!Number.isInteger(numeric) || numeric <= 0) {
    throw new AppError(`${field} must be a positive integer.`, 400);
  }
  return numeric;
};

const parsePositiveIntegerList = (value: unknown, field: string) => {
  if (!Array.isArray(value)) {
    throw new AppError(`${field} must be an array of positive integers.`, 400);
  }

  const parsed = value.map((entry) => parsePositiveInteger(entry, field));
  if (parsed.length === 0) {
    throw new AppError(`${field} must contain at least one session id.`, 400);
  }
  return parsed;
};

const parseNonEmptyString = (value: unknown, field: string) => {
  if (typeof value !== "string") {
    throw new AppError(`${field} must be a string.`, 400);
  }
  const trimmed = value.trim();
  if (!trimmed) {
    throw new AppError(`${field} cannot be empty.`, 400);
  }
  return trimmed;
};

const parseNullableString = (value: unknown, field: string) => {
  if (value === null) {
    return null;
  }
  if (typeof value !== "string") {
    throw new AppError(`${field} must be a string or null.`, 400);
  }
  const trimmed = value.trim();
  return trimmed || null;
};

const parseBoolean = (value: unknown, field: string) => {
  if (typeof value !== "boolean") {
    throw new AppError(`${field} must be a boolean.`, 400);
  }
  return value;
};

const requireAdmin = async (request: Request) => {
  const auth = requireAuth(request);
  const user = await authRepository.loadUserById(auth.userId);
  if (!user?.is_admin) {
    throw new AppError("Admin access required.", 403);
  }
  return auth;
};

export const listRecentSessionsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AdminRecentSessionsResponseDto>> => {
  await requireAdmin(ctx.request);
  const data = await adminService.listRecentSessions();
  return ok(data);
};

export const listSessionsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AdminSessionsListResponseDto>> => {
  await requireAdmin(ctx.request);
  const url = new URL(ctx.request.url);
  const query = (url.searchParams.get("query") ?? "").trim();
  const limitRaw = url.searchParams.get("limit");
  const offsetRaw = url.searchParams.get("offset");
  const parsedLimit = limitRaw ? Number(limitRaw) : 100;
  const parsedOffset = offsetRaw ? Number(offsetRaw) : 0;
  if (!Number.isInteger(parsedLimit) || parsedLimit <= 0) {
    throw new AppError("limit must be a positive integer.", 400);
  }
  if (!Number.isInteger(parsedOffset) || parsedOffset < 0) {
    throw new AppError("offset must be a non-negative integer.", 400);
  }
  const data = await adminService.listSessions(
    query,
    parsedLimit,
    parsedOffset
  );
  return ok(data);
};

export const listPlatformStatusesHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AdminPlatformStatusesResponseDto>> => {
  await requireAdmin(ctx.request);
  const data = await adminService.listPlatformStatuses();
  return ok(data);
};

export const updatePlatformStatusHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<AdminPlatformStatusUpdateResponseDto>> => {
  await requireAdmin(ctx.request);
  const payload = ctx.body as { platform?: unknown; enabled?: unknown } | null;
  const platform = parseNonEmptyString(payload?.platform, "platform");
  const enabled = parseBoolean(payload?.enabled, "enabled");
  const data = await adminService.updatePlatformStatus(platform, enabled);
  return ok(data);
};

export const listCredentialsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AdminCredentialsResponseDto>> => {
  await requireAdmin(ctx.request);
  const data = await adminService.listCredentials();
  return ok(data);
};

export const updateCredentialHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
  body?: unknown;
}): Promise<ApiResponse<AdminCredentialUpdateResponseDto>> => {
  await requireAdmin(ctx.request);
  const credentialId = parsePositiveInteger(
    ctx.params?.credentialId ?? "",
    "credentialId"
  );
  const payload = (ctx.body ?? {}) as Record<string, unknown>;
  const apiKeyChanged = Object.prototype.hasOwnProperty.call(payload, "apiKey");
  const userChanged = Object.prototype.hasOwnProperty.call(payload, "user");

  const data = await adminService.updateCredential(credentialId, {
    apiKeyChanged,
    apiKey: apiKeyChanged
      ? parseNullableString(payload.apiKey, "apiKey")
      : null,
    userChanged,
    user: userChanged ? parseNullableString(payload.user, "user") : null,
  });

  return ok(data);
};

export const moveSessionsHandler = async (ctx: {
  request: Request;
  body?: unknown;
}): Promise<ApiResponse<AdminSessionMoveResponseDto>> => {
  await requireAdmin(ctx.request);
  const payload = (ctx.body ?? {}) as {
    sessionIds?: unknown;
    targetCredentialId?: unknown;
  };

  const sessionIds = parsePositiveIntegerList(payload.sessionIds, "sessionIds");
  const targetCredentialId = parsePositiveInteger(
    payload.targetCredentialId,
    "targetCredentialId"
  );

  const data = await adminService.moveSessions(sessionIds, targetCredentialId);
  return ok(data);
};

export const searchGamesHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AdminGameSearchResponseDto>> => {
  await requireAdmin(ctx.request);
  const url = new URL(ctx.request.url);
  const query = (url.searchParams.get("query") ?? "").trim();
  if (!query) {
    throw new AppError("query is required.", 400);
  }
  const limitRaw = url.searchParams.get("limit");
  const parsedLimit = limitRaw ? Number(limitRaw) : 20;
  if (!Number.isInteger(parsedLimit) || parsedLimit <= 0) {
    throw new AppError("limit must be a positive integer.", 400);
  }
  const data = await adminService.searchGames(query, parsedLimit);
  return ok(data);
};

export const updateGameHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
  body?: unknown;
}): Promise<ApiResponse<AdminGameUpdateResponseDto>> => {
  await requireAdmin(ctx.request);
  const payload = (ctx.body ?? {}) as Record<string, unknown>;
  const url = new URL(ctx.request.url);
  const rawGameId =
    ctx.params?.gameId ??
    payload.gameId ??
    payload.id ??
    url.searchParams.get("gameId") ??
    "";
  const gameId = parseGameId(rawGameId, "gameId");

  const titleChanged = Object.prototype.hasOwnProperty.call(payload, "title");
  const imageUrlChanged = Object.prototype.hasOwnProperty.call(
    payload,
    "imageUrl"
  );
  const lockTitleChanged = Object.prototype.hasOwnProperty.call(
    payload,
    "lockTitle"
  );
  const lockImageChanged = Object.prototype.hasOwnProperty.call(
    payload,
    "lockImage"
  );

  const title = titleChanged ? parseNonEmptyString(payload.title, "title") : "";
  const imageUrl = imageUrlChanged
    ? parseNullableString(payload.imageUrl, "imageUrl")
    : null;
  const lockTitle = lockTitleChanged
    ? parseBoolean(payload.lockTitle, "lockTitle")
    : false;
  const lockImage = lockImageChanged
    ? parseBoolean(payload.lockImage, "lockImage")
    : false;

  const data = await adminService.updateGame(gameId, {
    titleChanged,
    title,
    lockTitleChanged,
    lockTitle,
    imageUrlChanged,
    imageUrl,
    lockImageChanged,
    lockImage,
  });
  return ok(data);
};
