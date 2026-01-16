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
  loadUserPlatformTotalsForRange,
  loadUserPlaytimeSummary,
  loadUserTopGames,
  loadUserPlaytimeSummaryForRange,
  loadUserTopGamesForRange,
  loadManualInputMappings,
  deleteUserSession,
  type AccountSessionFilters,
} from "../infrastructure/db/queries";
import { normalizeGameTitle } from "../domain/normalize-game";

type SessionRow = {
  sessionId: number;
  startedAt: Date;
  endedAt: Date | null;
  platform: string;
  game: string;
  capsuleUrl: string | null;
  gamePlatform?: string;
  gameCanonicalKey?: string;
  mappedPlatform?: string;
  mappedSource?: "local" | "igdb";
};

const stripSessionMetadata = (session: SessionRow) => {
  const { gamePlatform, gameCanonicalKey, ...rest } = session;
  return rest;
};

const applyManualMappings = async (userId: number, sessions: SessionRow[]) => {
  const manualSessions = sessions.filter(
    (session) => session.platform === "manual"
  );
  if (manualSessions.length === 0) {
    return sessions.map(stripSessionMetadata);
  }

  const canonicalTitles = Array.from(
    new Set(manualSessions.map((session) => normalizeGameTitle(session.game)))
  );
  if (canonicalTitles.length === 0) {
    return sessions.map(stripSessionMetadata);
  }

  const mappings = await loadManualInputMappings(userId, canonicalTitles);
  const mappingIndex = new Map(
    mappings
      .filter((row) => row.mapped_game_id && row.mapped_title)
      .map((row) => [
        row.canonical_title,
        {
          title: row.mapped_title ?? "",
          platform: row.mapped_platform ?? "manual",
          capsuleUrl: row.mapped_capsule_url,
          source: row.mapped_canonical_key?.startsWith("igdb:")
            ? "igdb"
            : "local",
        },
      ])
  );

  return sessions.map((session) => {
    if (session.platform !== "manual") {
      return stripSessionMetadata(session);
    }
    const mapping = mappingIndex.get(normalizeGameTitle(session.game));
    if (!mapping) {
      if (session.gamePlatform && session.gamePlatform !== "manual") {
        return stripSessionMetadata({
          ...session,
          mappedPlatform: session.gamePlatform,
          mappedSource: session.gameCanonicalKey?.startsWith("igdb:")
            ? "igdb"
            : "local",
        });
      }
      return stripSessionMetadata(session);
    }
    return stripSessionMetadata({
      ...session,
      game: mapping.title,
      capsuleUrl: mapping.capsuleUrl ?? session.capsuleUrl,
      mappedPlatform: mapping.platform,
      mappedSource: mapping.source,
    });
  });
};

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
  const sessions = rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
    gamePlatform: row.game_platform,
    gameCanonicalKey: row.game_canonical_key,
  }));
  return applyManualMappings(userId, sessions);
};

export const getUserSessions = async (
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {}
) => {
  const rows = await listUserSessions(userId, limit, offset, filters);
  const sessions = rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
    gamePlatform: row.game_platform,
    gameCanonicalKey: row.game_canonical_key,
  }));
  return applyManualMappings(userId, sessions);
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
  const sessions = rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
    gamePlatform: row.game_platform,
    gameCanonicalKey: row.game_canonical_key,
  }));
  return applyManualMappings(userId, sessions);
};

export const getUserSessionsToday = async (userId: number, limit: number) => {
  const rows = await listUserSessionsToday(userId, limit);
  const sessions = rows.map((row) => ({
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
    gamePlatform: row.game_platform,
    gameCanonicalKey: row.game_canonical_key,
  }));
  return applyManualMappings(userId, sessions);
};

export const getAccountCurrentSession = async (
  accountId: number,
  userId: number
) => {
  const row = await loadAccountCurrentSession(accountId, userId);
  if (!row) {
    return null;
  }
  const session = {
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
    gamePlatform: row.game_platform,
    gameCanonicalKey: row.game_canonical_key,
  };
  const [mapped] = await applyManualMappings(userId, [session]);
  return mapped ?? null;
};

export const getUserCurrentSession = async (userId: number) => {
  const row = await loadUserCurrentSession(userId);
  if (!row) {
    return null;
  }
  const session = {
    sessionId: +row.id,
    startedAt: row.started_at,
    endedAt: row.ended_at,
    platform: row.platform,
    game: row.title,
    capsuleUrl: row.capsule_url,
    gamePlatform: row.game_platform,
    gameCanonicalKey: row.game_canonical_key,
  };
  const [mapped] = await applyManualMappings(userId, [session]);
  return mapped ?? null;
};

export const removeUserSession = async (userId: number, sessionId: number) => {
  const deletedId = await deleteUserSession(sessionId, userId);
  return { deleted: Boolean(deletedId), sessionId: deletedId };
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
      source: row.source,
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
      source: row.source,
      totalSeconds: row.total_seconds ?? 0,
    })),
    topGames: topGameRows.map((row) => ({
      title: row.title,
      totalSeconds: row.total_seconds ?? 0,
    })),
  };
};

const buildUserStatsRange = async (
  userId: number,
  startedAt: Date,
  endedAt: Date
) => {
  const [summaryRows, platformRows, topGameRows] = await Promise.all([
    loadUserPlaytimeSummaryForRange(userId, startedAt, endedAt),
    loadUserPlatformTotalsForRange(userId, startedAt, endedAt),
    loadUserTopGamesForRange(userId, startedAt, endedAt, 5),
  ]);

  const summary = summaryRows[0] ?? { total_seconds: 0, session_count: 0 };
  return {
    totalSeconds: summary.total_seconds ?? 0,
    sessionCount: summary.session_count ?? 0,
    topGameTitle: topGameRows[0]?.title ?? null,
    platforms: platformRows.map((row) => ({
      platform: row.platform,
      source: row.source,
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

export const getUserStatsForRange = async (
  userId: number,
  startedAt: Date,
  endedAt: Date
) => buildUserStatsRange(userId, startedAt, endedAt);
