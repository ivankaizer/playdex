import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type {
  AccountCurrentSessionResponseDto,
  AccountSessionDeleteResponseDto,
  AccountSessionsResponseDto,
  AccountStatsRangeResponseDto,
  AccountStatsResponseDto,
  AccountSummaryDto,
  AccountTodaySessionsResponseDto,
  ApiResponse,
} from "../../app/http/types";
import { AppError } from "../../common/errors/AppError";
import { accountsService } from "./accounts.service";
import type { RawAccountSessionFilters } from "./accounts.repository";

const parseAccountId = (params: Record<string, string> | undefined) => {
  const raw = params?.accountId?.trim() ?? "";
  const accountId = Number(raw);
  if (!raw || !Number.isInteger(accountId)) {
    throw new AppError("accountId must be a number.", 400);
  }
  return accountId;
};

const parseSessionId = (params: Record<string, string> | undefined) => {
  const raw = params?.sessionId?.trim() ?? "";
  const sessionId = Number(raw);
  if (!raw || !Number.isInteger(sessionId)) {
    throw new AppError("sessionId must be a number.", 400);
  }
  return sessionId;
};

const parsePaginationAndFilters = (
  url: URL
): { limit: number; offset: number; filters: RawAccountSessionFilters } => {
  const limitRaw = url.searchParams.get("limit");
  const offsetRaw = url.searchParams.get("offset");
  const sortRaw = url.searchParams.get("sort");
  const directionRaw = url.searchParams.get("direction");
  const title = url.searchParams.get("title")?.trim() || null;
  const fromRaw = url.searchParams.get("from");
  const toRaw = url.searchParams.get("to");

  const limit = limitRaw ? Number(limitRaw) : 10;
  const offset = offsetRaw ? Number(offsetRaw) : 0;

  if (
    !Number.isFinite(limit) ||
    !Number.isFinite(offset) ||
    limit <= 0 ||
    offset < 0
  ) {
    throw new AppError("limit and offset must be valid numbers.", 400);
  }

  const safeLimit = Math.min(Math.floor(limit), 100);
  const safeOffset = Math.floor(offset);

  const parsedFrom = fromRaw ? new Date(fromRaw) : null;
  const parsedTo = toRaw ? new Date(toRaw) : null;

  if (
    (parsedFrom && Number.isNaN(parsedFrom.getTime())) ||
    (parsedTo && Number.isNaN(parsedTo.getTime()))
  ) {
    throw new AppError("from and to must be valid dates.", 400);
  }

  const sortKey =
    sortRaw === "date" || sortRaw === "duration" ? sortRaw : null;
  if (sortRaw && !sortKey) {
    throw new AppError("sort must be one of: date, duration.", 400);
  }

  const sortDirection =
    directionRaw === "asc" || directionRaw === "desc" ? directionRaw : null;
  if (directionRaw && !sortDirection) {
    throw new AppError("direction must be one of: asc, desc.", 400);
  }

  if (parsedFrom && parsedTo && parsedFrom > parsedTo) {
    throw new AppError("from must be before to.", 400);
  }

  const startedTo = parsedTo
    ? new Date(
        Date.UTC(
          parsedTo.getUTCFullYear(),
          parsedTo.getUTCMonth(),
          parsedTo.getUTCDate() + 1
        )
      )
    : null;

  const filters: RawAccountSessionFilters = {
    title: title ?? undefined,
    startedFrom: parsedFrom ?? undefined,
    startedTo: startedTo ?? undefined,
    sortKey: sortKey ?? undefined,
    sortDirection: sortDirection ?? undefined,
  };

  return { limit: safeLimit, offset: safeOffset, filters };
};

const parseStatsRange = (url: URL) => {
  const fromRaw = url.searchParams.get("from");
  const toRaw = url.searchParams.get("to");
  const parsedFrom = fromRaw ? new Date(fromRaw) : null;
  const parsedTo = toRaw ? new Date(toRaw) : null;

  if (!parsedFrom || !parsedTo) {
    throw new AppError("from and to are required.", 400);
  }

  if (
    Number.isNaN(parsedFrom.getTime()) ||
    Number.isNaN(parsedTo.getTime())
  ) {
    throw new AppError("from and to must be valid dates.", 400);
  }

  if (parsedFrom > parsedTo) {
    throw new AppError("from must be before to.", 400);
  }

  const endedAt = new Date(
    Date.UTC(
      parsedTo.getUTCFullYear(),
      parsedTo.getUTCMonth(),
      parsedTo.getUTCDate() + 1
    )
  );

  return { startedAt: parsedFrom, endedAt };
};

export const listAccountsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AccountSummaryDto[]>> => {
  const auth = requireAuth(ctx.request);
  const accounts = await accountsService.listAccounts(auth.userId);
  return ok(accounts);
};

export const listUserSessionsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AccountSessionsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const { limit, offset, filters } = parsePaginationAndFilters(
    new URL(ctx.request.url)
  );
  const { sessions, total } = await accountsService.fetchUserSessions(
    auth.userId,
    limit,
    offset,
    filters
  );

  const response: AccountSessionsResponseDto = {
    accountId: null,
    sessions,
    total,
    limit,
    offset,
  };
  return ok(response);
};

export const deleteUserSessionHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
}): Promise<ApiResponse<AccountSessionDeleteResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const sessionId = parseSessionId(ctx.params);
  const result = await accountsService.removeUserSession(
    auth.userId,
    sessionId
  );

  if (!result.deleted) {
    throw new AppError("Session not found.", 404);
  }

  return ok({ sessionId: result.sessionId });
};

export const listUserSessionsTodayHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AccountTodaySessionsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const sessions = await accountsService.fetchUserSessionsToday(auth.userId, 10);
  return ok({ accountId: null, sessions });
};

export const getUserCurrentSessionHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AccountCurrentSessionResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const session = await accountsService.fetchUserCurrentSession(auth.userId);
  return ok({ accountId: null, session });
};

export const getUserStatsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<AccountStatsRangeResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const { startedAt, endedAt } = parseStatsRange(new URL(ctx.request.url));
  const stats = await accountsService.fetchUserStatsForRange(
    auth.userId,
    startedAt,
    endedAt
  );
  return ok({ accountId: null, stats });
};

export const listAccountSessionsHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
}): Promise<ApiResponse<AccountSessionsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const accountId = parseAccountId(ctx.params);
  const { limit, offset, filters } = parsePaginationAndFilters(
    new URL(ctx.request.url)
  );
  const { sessions, total } = await accountsService.fetchAccountSessions(
    accountId,
    auth.userId,
    limit,
    offset,
    filters
  );

  const response: AccountSessionsResponseDto = {
    accountId,
    sessions,
    total,
    limit,
    offset,
  };
  return ok(response);
};

export const listAccountSessionsTodayHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
}): Promise<ApiResponse<AccountTodaySessionsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const accountId = parseAccountId(ctx.params);
  const sessions = await accountsService.fetchAccountSessionsToday(
    accountId,
    auth.userId,
    10
  );
  return ok({ accountId, sessions });
};

export const getAccountCurrentSessionHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
}): Promise<ApiResponse<AccountCurrentSessionResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const accountId = parseAccountId(ctx.params);
  const session = await accountsService.fetchAccountCurrentSession(
    accountId,
    auth.userId
  );
  return ok({ accountId, session });
};

export const getAccountStatsHandler = async (ctx: {
  request: Request;
  params?: Record<string, string>;
}): Promise<ApiResponse<AccountStatsResponseDto>> => {
  const auth = requireAuth(ctx.request);
  const accountId = parseAccountId(ctx.params);
  const stats = await accountsService.fetchAccountStats(accountId, auth.userId);
  return ok({ accountId, monthly: stats.monthly, yearly: stats.yearly });
};

