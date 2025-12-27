import type { Elysia } from "elysia";

import {
  getAccountSessions,
  getAccountSessionsToday,
  getAccountSessionsTotal,
  getAccountCurrentSession,
  getAccountSummaries,
  getAccountStats,
} from "../../application/accounts";
import type {
  AccountCurrentSessionResponseDto,
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
        const titleRaw = url.searchParams.get("title");
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
          set.status = 400;
          return fail("limit and offset must be valid numbers.");
        }

        const safeLimit = Math.min(Math.floor(limit), 100);
        const safeOffset = Math.floor(offset);

        const title = titleRaw?.trim() ? titleRaw.trim() : null;
        const parsedFrom = fromRaw ? new Date(fromRaw) : null;
        const parsedTo = toRaw ? new Date(toRaw) : null;

        if (
          (parsedFrom && Number.isNaN(parsedFrom.getTime())) ||
          (parsedTo && Number.isNaN(parsedTo.getTime()))
        ) {
          set.status = 400;
          return fail("from and to must be valid dates.");
        }

        if (parsedFrom && parsedTo && parsedFrom > parsedTo) {
          set.status = 400;
          return fail("from must be before to.");
        }

        const toExclusive = parsedTo
          ? new Date(
              Date.UTC(
                parsedTo.getUTCFullYear(),
                parsedTo.getUTCMonth(),
                parsedTo.getUTCDate() + 1
              )
            )
          : null;

        const [sessions, total] = await Promise.all([
          getAccountSessions(
            parsed.accountId,
            auth.userId,
            safeLimit,
            safeOffset,
            {
              title,
              startedFrom: parsedFrom,
              startedTo: toExclusive,
            }
          ),
          getAccountSessionsTotal(parsed.accountId, auth.userId, {
            title,
            startedFrom: parsedFrom,
            startedTo: toExclusive,
          }),
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
      "/api/accounts/:accountId/sessions/current",
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

        const session = await getAccountCurrentSession(
          parsed.accountId,
          auth.userId
        );

        const response: AccountCurrentSessionResponseDto = {
          accountId: parsed.accountId,
          session,
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
