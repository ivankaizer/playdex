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
import { gamesService } from "../games";
import {
  mergeGameTotalsByConnections,
  resolveConnectedGameIds,
} from "../games/gameConnections";

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

const resolveSessionFilters = async (
  userId: number,
  filters: RawAccountSessionFilters
) => {
  if (!filters.gameId) {
    return filters;
  }

  const edges = await gamesService.listConnectionEdges(userId);
  const connectedIds = resolveConnectedGameIds(filters.gameId, edges);

  return {
    ...filters,
    gameIds: connectedIds,
  };
};

const mergeTopGames = (
  rows: { game_id: number; title: string; total_seconds: number }[],
  limit: number,
  edges: { game_id: number; connected_game_id: number }[]
) => {
  if (rows.length === 0) {
    return [];
  }
  const normalizedRows = rows.map((row) => ({
    ...row,
    game_id: Number(row.game_id),
  }));
  const merged =
    edges.length === 0
      ? normalizedRows
      : mergeGameTotalsByConnections(normalizedRows, edges);
  return merged.slice(0, limit);
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
    const resolvedFilters = await resolveSessionFilters(userId, filters);
    const rows = await accountsRepository.listUserSessions(
      userId,
      limit,
      offset,
      resolvedFilters
    );
    const [sessions, total] = await Promise.all([
      toAccountSessions(userId, rows),
      accountsRepository.countUserSessions(userId, resolvedFilters),
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
    const resolvedFilters = await resolveSessionFilters(userId, filters);
    const rows = await accountsRepository.listAccountSessions(
      accountId,
      userId,
      limit,
      offset,
      resolvedFilters
    );
    const [sessions, total] = await Promise.all([
      toAccountSessions(userId, rows),
      accountsRepository.countAccountSessions(
        accountId,
        userId,
        resolvedFilters
      ),
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
        accountsRepository.loadAccountGameTotals(accountId, userId, monthStart),
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
        accountsRepository.loadAccountGameTotals(accountId, userId, yearStart),
      ]),
    ]);

    const connectionEdges = await gamesService.listConnectionEdges(userId);
    const monthlyTopGames = mergeTopGames(monthly[2], 5, connectionEdges);
    const yearlyTopGames = mergeTopGames(yearly[2], 5, connectionEdges);

    return {
      monthly: buildAccountStatsPeriod(monthly[0], monthly[1], monthlyTopGames),
      yearly: buildAccountStatsPeriod(yearly[0], yearly[1], yearlyTopGames),
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
      accountsRepository.loadUserGameTotalsForRange(userId, startedAt, endedAt),
    ]);

    const connectionEdges = await gamesService.listConnectionEdges(userId);
    const mergedTopGames = mergeTopGames(topGameRows, 5, connectionEdges);

    return buildUserStatsPeriod(summaryRows, platformRows, mergedTopGames);
  },
};
