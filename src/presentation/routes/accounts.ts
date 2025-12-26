import type { Elysia } from "elysia";

import {
  getAccountSessions,
  getAccountSessionsToday,
  getAccountSessionsTotal,
  getAccountSummaries,
  getAccountStats,
} from "../../application/accounts";
import type {
  AccountSessionsResponseDto,
  AccountStatsResponseDto,
  AccountSummaryDto,
  AccountTodaySessionsResponseDto,
} from "./types";
import {
  fail,
  ok,
  parseAccountIdParam,
  requireAuth,
  withApiErrorLogging,
} from "./utils";

export const registerAccountRoutes = (app: Elysia) =>
  app
    .get(
      "/api/accounts",
      withApiErrorLogging(async ({ request, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const rows = await getAccountSummaries(auth.userId);
        const response: AccountSummaryDto[] = rows;
        return ok(response);
      })
    )
    .get(
      "/api/accounts/:accountId/sessions",
      withApiErrorLogging(async ({ request, params = {}, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const parsed = parseAccountIdParam(params);
        if (!parsed.ok) {
          set.status = 400;
          return parsed.response;
        }

        const url = new URL(request.url);
        const limitRaw = url.searchParams.get("limit");
        const offsetRaw = url.searchParams.get("offset");
        const limit = limitRaw ? Number(limitRaw) : 10;
        const offset = offsetRaw ? Number(offsetRaw) : 0;

        if (
          !Number.isFinite(limit) ||
          !Number.isFinite(offset) ||
          limit <= 0 ||
          offset < 0
        ) {
          set.status = 400;
          return fail("limit and offset must be valid numbers.");
        }

        const safeLimit = Math.min(Math.floor(limit), 100);
        const safeOffset = Math.floor(offset);

        const [sessions, total] = await Promise.all([
          getAccountSessions(
            parsed.accountId,
            auth.userId,
            safeLimit,
            safeOffset
          ),
          getAccountSessionsTotal(parsed.accountId, auth.userId),
        ]);

        const response: AccountSessionsResponseDto = {
          accountId: parsed.accountId,
          sessions,
          total,
          limit: safeLimit,
          offset: safeOffset,
        };
        return ok(response);
      })
    )
    .get(
      "/api/accounts/:accountId/sessions/today",
      withApiErrorLogging(async ({ request, params = {}, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const parsed = parseAccountIdParam(params);
        if (!parsed.ok) {
          set.status = 400;
          return parsed.response;
        }

        const sessions = await getAccountSessionsToday(
          parsed.accountId,
          auth.userId,
          10
        );

        const response: AccountTodaySessionsResponseDto = {
          accountId: parsed.accountId,
          sessions,
        };
        return ok(response);
      })
    )
    .get(
      "/api/accounts/:accountId/stats",
      withApiErrorLogging(async ({ request, params = {}, set }) => {
        const auth = requireAuth(request);
        if (!auth.ok) {
          set.status = auth.status;
          return auth.response;
        }

        const parsed = parseAccountIdParam(params);
        if (!parsed.ok) {
          set.status = 400;
          return parsed.response;
        }

        const stats = await getAccountStats(parsed.accountId, auth.userId);
        const response: AccountStatsResponseDto = {
          accountId: parsed.accountId,
          monthly: stats.monthly,
          yearly: stats.yearly,
        };
        return ok(response);
      })
    );
