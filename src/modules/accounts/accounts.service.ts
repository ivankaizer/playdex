import type {
  AccountStatsPeriod,
  AccountSession,
  AccountSessionWithMetadata,
} from "./accounts.domain";
import {
  applyManualMappings,
  buildAccountStatsPeriod,
  buildUserStatsPeriod,
  collectManualCanonicalTitles,
  toDomainSession,
} from "./accounts.domain";
import {
  accountsRepository,
  type RawAccountSessionRow,
  type RawAccountSessionFilters,
} from "./accounts.repository";

const mapSessionsForUser = async (
  userId: number,
  rows: AccountSessionWithMetadata[]
) => {
  const manualTitles = collectManualCanonicalTitles(rows);
  const mappings =
    manualTitles.length > 0
      ? await accountsRepository.loadManualMappings(userId, manualTitles)
      : [];
  return applyManualMappings(rows, mappings);
};

const toAccountSessions = async (
  userId: number,
  rows: RawAccountSessionRow[]
) => {
  const domainSessions = rows.map((row) => toDomainSession(row));
  return mapSessionsForUser(userId, domainSessions);
};

export const accountsService = {
  listAccounts: async (userId: number) => {
    const rows = await accountsRepository.listAccounts(userId);
    return rows.map((row) => ({ id: +row.id, name: row.platform }));
  },

  fetchUserSessions: async (
    userId: number,
    limit: number,
    offset: number,
    filters: RawAccountSessionFilters
  ): Promise<{ sessions: AccountSession[]; total: number }> => {
    const rows = await accountsRepository.listUserSessions(
      userId,
      limit,
      offset,
      filters
    );
    const [sessions, total] = await Promise.all([
      toAccountSessions(userId, rows),
      accountsRepository.countUserSessions(userId, filters),
    ]);
    return { sessions, total };
  },

  fetchAccountSessions: async (
    accountId: number,
    userId: number,
    limit: number,
    offset: number,
    filters: RawAccountSessionFilters
  ): Promise<{ sessions: AccountSession[]; total: number }> => {
    const rows = await accountsRepository.listAccountSessions(
      accountId,
      userId,
      limit,
      offset,
      filters
    );
    const [sessions, total] = await Promise.all([
      toAccountSessions(userId, rows),
      accountsRepository.countAccountSessions(accountId, userId, filters),
    ]);
    return { sessions, total };
  },

  fetchUserSessionsToday: async (
    userId: number,
    limit: number
  ): Promise<AccountSession[]> => {
    const rows = await accountsRepository.listUserSessionsToday(userId, limit);
    return toAccountSessions(userId, rows);
  },

  fetchAccountSessionsToday: async (
    accountId: number,
    userId: number,
    limit: number
  ): Promise<AccountSession[]> => {
    const rows = await accountsRepository.listAccountSessionsToday(
      accountId,
      userId,
      limit
    );
    return toAccountSessions(userId, rows);
  },

  fetchUserCurrentSession: async (
    userId: number
  ): Promise<AccountSession | null> => {
    const row = await accountsRepository.getUserCurrentSession(userId);
    if (!row) {
      return null;
    }
    const [session] = await toAccountSessions(userId, [row]);
    return session ?? null;
  },

  fetchAccountCurrentSession: async (
    accountId: number,
    userId: number
  ): Promise<AccountSession | null> => {
    const row = await accountsRepository.getAccountCurrentSession(
      accountId,
      userId
    );
    if (!row) {
      return null;
    }
    const [session] = await toAccountSessions(userId, [row]);
    return session ?? null;
  },

  removeUserSession: async (userId: number, sessionId: number) => {
    const deletedId = await accountsRepository.deleteUserSession(
      sessionId,
      userId
    );
    return { deleted: Boolean(deletedId), sessionId: deletedId ?? sessionId };
  },

  fetchAccountStats: async (
    accountId: number,
    userId: number
  ): Promise<{ monthly: AccountStatsPeriod; yearly: AccountStatsPeriod }> => {
    const now = new Date();
    const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
    const yearStart = new Date(now.getFullYear(), 0, 1);

    const [monthly, yearly] = await Promise.all([
      Promise.all([
        accountsRepository.loadAccountPlaytimeSummary(
          accountId,
          userId,
          monthStart
        ),
        accountsRepository.loadAccountPlatformTotals(
          accountId,
          userId,
          monthStart
        ),
        accountsRepository.loadAccountTopGames(
          accountId,
          userId,
          monthStart,
          5
        ),
      ]),
      Promise.all([
        accountsRepository.loadAccountPlaytimeSummary(
          accountId,
          userId,
          yearStart
        ),
        accountsRepository.loadAccountPlatformTotals(
          accountId,
          userId,
          yearStart
        ),
        accountsRepository.loadAccountTopGames(
          accountId,
          userId,
          yearStart,
          5
        ),
      ]),
    ]);

    return {
      monthly: buildAccountStatsPeriod(monthly[0], monthly[1], monthly[2]),
      yearly: buildAccountStatsPeriod(yearly[0], yearly[1], yearly[2]),
    };
  },

  fetchUserStatsForRange: async (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ): Promise<AccountStatsPeriod> => {
    const [summaryRows, platformRows, topGameRows] = await Promise.all([
      accountsRepository.loadUserPlaytimeSummaryForRange(
        userId,
        startedAt,
        endedAt
      ),
      accountsRepository.loadUserPlatformTotalsForRange(
        userId,
        startedAt,
        endedAt
      ),
      accountsRepository.loadUserTopGamesForRange(userId, startedAt, endedAt, 5),
    ]);

    return buildUserStatsPeriod(summaryRows, platformRows, topGameRows);
  },
};
