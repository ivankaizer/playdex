import {
  healthCheck,
  countAccountSessions,
  countUserSessions,
  listAccountSessions,
  listAccountSessionsToday,
  listUserSessions,
  listUserSessionsToday,
  listAccountsForUser,
  loadAccountCurrentSession,
  loadAccountPlatformTotals,
  loadAccountPlaytimeSummary,
  loadAccountTopGames,
  loadUserCurrentSession,
  loadUserPlatformTotals,
  loadUserPlaytimeSummary,
  loadUserTopGames,
  type AccountSessionFilters,
} from "../infrastructure/db/queries";

export const checkDatabaseHealth = async () => {
  await healthCheck();
};

export const getAccountSummaries = async (userId: number) => {
  const rows = await listAccountsForUser(userId);
  return rows.map((row) => ({ id: +row.id, name: row.platform }));
};

export const getAccountSessions = async (
  accountId: number,
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {}
) => {
  const rows = await listAccountSessions(
    accountId,
    userId,
    limit,
    offset,
    filters
  );
  return rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
  }));
};

export const getUserSessions = async (
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {}
) => {
  const rows = await listUserSessions(userId, limit, offset, filters);
  return rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
  }));
};

export const getAccountSessionsTotal = async (
  accountId: number,
  userId: number,
  filters: AccountSessionFilters = {}
) => {
  const rows = await countAccountSessions(accountId, userId, filters);
  return rows[0]?.total ?? 0;
};

export const getUserSessionsTotal = async (
  userId: number,
  filters: AccountSessionFilters = {}
) => {
  const rows = await countUserSessions(userId, filters);
  return rows[0]?.total ?? 0;
};

export const getAccountSessionsToday = async (
  accountId: number,
  userId: number,
  limit: number
) => {
  const rows = await listAccountSessionsToday(accountId, userId, limit);
  return rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
  }));
};

export const getUserSessionsToday = async (userId: number, limit: number) => {
  const rows = await listUserSessionsToday(userId, limit);
  return rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
  }));
};

export const getAccountCurrentSession = async (
  accountId: number,
  userId: number
) => {
  const row = await loadAccountCurrentSession(accountId, userId);
  if (!row) {
    return null;
  }
  return {
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
  };
};

export const getUserCurrentSession = async (userId: number) => {
  const row = await loadUserCurrentSession(userId);
  if (!row) {
    return null;
  }
  return {
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
  };
};

const buildStatsPeriod = async (
  accountId: number,
  userId: number,
  startedAt: Date
) => {
  const [summaryRows, platformRows, topGameRows] = await Promise.all([
    loadAccountPlaytimeSummary(accountId, userId, startedAt),
    loadAccountPlatformTotals(accountId, userId, startedAt),
    loadAccountTopGames(accountId, userId, startedAt, 5),
  ]);

  const summary = summaryRows[0] ?? { total_seconds: 0, session_count: 0 };
  return {
    totalSeconds: summary.total_seconds ?? 0,
    sessionCount: summary.session_count ?? 0,
    topGameTitle: topGameRows[0]?.title ?? null,
    platforms: platformRows.map((row) => ({
      platform: row.platform,
      totalSeconds: row.total_seconds ?? 0,
    })),
    topGames: topGameRows.map((row) => ({
      title: row.title,
      totalSeconds: row.total_seconds ?? 0,
    })),
  };
};

const buildUserStatsPeriod = async (userId: number, startedAt: Date) => {
  const [summaryRows, platformRows, topGameRows] = await Promise.all([
    loadUserPlaytimeSummary(userId, startedAt),
    loadUserPlatformTotals(userId, startedAt),
    loadUserTopGames(userId, startedAt, 5),
  ]);

  const summary = summaryRows[0] ?? { total_seconds: 0, session_count: 0 };
  return {
    totalSeconds: summary.total_seconds ?? 0,
    sessionCount: summary.session_count ?? 0,
    topGameTitle: topGameRows[0]?.title ?? null,
    platforms: platformRows.map((row) => ({
      platform: row.platform,
      totalSeconds: row.total_seconds ?? 0,
    })),
    topGames: topGameRows.map((row) => ({
      title: row.title,
      totalSeconds: row.total_seconds ?? 0,
    })),
  };
};

export const getAccountStats = async (accountId: number, userId: number) => {
  const now = new Date();
  const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
  const yearStart = new Date(now.getFullYear(), 0, 1);

  const [monthly, yearly] = await Promise.all([
    buildStatsPeriod(accountId, userId, monthStart),
    buildStatsPeriod(accountId, userId, yearStart),
  ]);

  return { monthly, yearly };
};

export const getUserStats = async (userId: number) => {
  const now = new Date();
  const monthStart = new Date(now.getFullYear(), now.getMonth(), 1);
  const yearStart = new Date(now.getFullYear(), 0, 1);

  const [monthly, yearly] = await Promise.all([
    buildUserStatsPeriod(userId, monthStart),
    buildUserStatsPeriod(userId, yearStart),
  ]);

  return { monthly, yearly };
};
