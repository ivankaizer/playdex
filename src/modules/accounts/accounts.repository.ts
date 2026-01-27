import {
  countAccountSessions,
  countUserSessions,
  deleteUserSession,
  listAccountSessions,
  listAccountSessionsToday,
  listAccountsForUser,
  listUserSessions,
  listUserSessionsToday,
  loadAccountCurrentSession,
  loadAccountPlatformTotals,
  loadAccountPlaytimeSummary,
  loadAccountTopGames,
  loadManualInputMappings,
  loadUserCurrentSession,
  loadUserPlatformTotals,
  loadUserPlatformTotalsForRange,
  loadUserPlaytimeSummary,
  loadUserPlaytimeSummaryForRange,
  loadUserTopGames,
  loadUserTopGamesForRange,
  type AccountPlaytimeSummaryRow,
  type AccountPlatformTotalsRow,
  type AccountSessionCountRow,
  type AccountSessionFilters,
  type AccountSessionRow,
  type AccountTopGameRow,
  type ManualInputMappingRow,
} from "../../shared/db/queries";

export type RawAccountSessionRow = AccountSessionRow;
export type RawAccountSessionFilters = AccountSessionFilters;
export type RawManualInputMappingRow = ManualInputMappingRow;
export type RawAccountPlaytimeSummaryRow = AccountPlaytimeSummaryRow;
export type RawAccountPlatformTotalsRow = AccountPlatformTotalsRow;
export type RawAccountTopGameRow = AccountTopGameRow;

const mapCount = (rows: AccountSessionCountRow[]) => rows[0]?.total ?? 0;

export const accountsRepository = {
  listAccounts: (userId: number) => listAccountsForUser(userId),

  listAccountSessions: (
    accountId: number,
    userId: number,
    limit: number,
    offset: number,
    filters: RawAccountSessionFilters
  ) => listAccountSessions(accountId, userId, limit, offset, filters),

  listUserSessions: (
    userId: number,
    limit: number,
    offset: number,
    filters: RawAccountSessionFilters
  ) => listUserSessions(userId, limit, offset, filters),

  countAccountSessions: (
    accountId: number,
    userId: number,
    filters: RawAccountSessionFilters
  ) =>
    countAccountSessions(accountId, userId, filters).then((rows) =>
      mapCount(rows)
    ),

  countUserSessions: (userId: number, filters: RawAccountSessionFilters) =>
    countUserSessions(userId, filters).then((rows) => mapCount(rows)),

  listAccountSessionsToday: (
    accountId: number,
    userId: number,
    limit: number
  ) => listAccountSessionsToday(accountId, userId, limit),

  listUserSessionsToday: (userId: number, limit: number) =>
    listUserSessionsToday(userId, limit),

  getAccountCurrentSession: (accountId: number, userId: number) =>
    loadAccountCurrentSession(accountId, userId),

  getUserCurrentSession: (userId: number) => loadUserCurrentSession(userId),

  deleteUserSession: (sessionId: number, userId: number) =>
    deleteUserSession(sessionId, userId),

  loadManualMappings: (userId: number, canonicalTitles: string[]) =>
    loadManualInputMappings(userId, canonicalTitles),

  loadAccountPlaytimeSummary: (
    accountId: number,
    userId: number,
    startedAt: Date
  ) => loadAccountPlaytimeSummary(accountId, userId, startedAt),

  loadAccountPlatformTotals: (
    accountId: number,
    userId: number,
    startedAt: Date
  ) => loadAccountPlatformTotals(accountId, userId, startedAt),

  loadAccountTopGames: (
    accountId: number,
    userId: number,
    startedAt: Date,
    limit: number
  ) => loadAccountTopGames(accountId, userId, startedAt, limit),

  loadUserPlaytimeSummary: (userId: number, startedAt: Date) =>
    loadUserPlaytimeSummary(userId, startedAt),

  loadUserPlatformTotals: (userId: number, startedAt: Date) =>
    loadUserPlatformTotals(userId, startedAt),

  loadUserTopGames: (userId: number, startedAt: Date, limit: number) =>
    loadUserTopGames(userId, startedAt, limit),

  loadUserPlaytimeSummaryForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ) => loadUserPlaytimeSummaryForRange(userId, startedAt, endedAt),

  loadUserPlatformTotalsForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ) => loadUserPlatformTotalsForRange(userId, startedAt, endedAt),

  loadUserTopGamesForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date,
    limit: number
  ) => loadUserTopGamesForRange(userId, startedAt, endedAt, limit),
};

