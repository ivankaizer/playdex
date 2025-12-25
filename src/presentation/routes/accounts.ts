import type { Elysia } from "elysia";

import {
  getAccountSessions,
  getAccountSummaries,
} from "../../application/accounts";
import type { AccountSessionsResponseDto, AccountSummaryDto } from "./types";
import { ok, parseAccountIdParam, withApiErrorLogging } from "./utils";

export const registerAccountRoutes = (app: Elysia) =>
  app
    .get(
      "/api/accounts",
      withApiErrorLogging(async () => {
        const rows = await getAccountSummaries();
        const response: AccountSummaryDto[] = rows;
        return ok(response);
      })
    )
    .get(
      "/api/accounts/:accountId/sessions",
      withApiErrorLogging(async ({ params = {}, set }) => {
        const parsed = parseAccountIdParam(params);
        if (!parsed.ok) {
          set.status = 400;
          return parsed.response;
        }

        const sessions = await getAccountSessions(parsed.accountId);

        const response: AccountSessionsResponseDto = {
          accountId: parsed.accountId,
          sessions,
        };
        return ok(response);
      })
    );
