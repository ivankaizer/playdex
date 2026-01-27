import { normalizeGameTitle } from "../games";
import type {
  RawAccountPlatformTotalsRow,
  RawAccountPlaytimeSummaryRow,
  RawAccountSessionRow,
  RawAccountTopGameRow,
  RawManualInputMappingRow,
} from "./accounts.repository";

export type AccountSession = {
  sessionId: number;
  startedAt: Date;
  endedAt: Date | null;
  gameId: number;
  platform: string;
  game: string;
  capsuleUrl: string | null;
  mappedPlatform?: string;
  mappedSource?: "local" | "igdb";
};

export type AccountSessionWithMetadata = AccountSession & {
  gamePlatform?: string;
  gameCanonicalKey?: string;
};

export const toDomainSession = (
  row: RawAccountSessionRow
): AccountSessionWithMetadata => ({
  sessionId: +row.id,
  startedAt: row.started_at,
  endedAt: row.ended_at,
  gameId: row.game_id,
  platform: row.platform,
  game: row.title,
  capsuleUrl: row.capsule_url,
  gamePlatform: row.game_platform,
  gameCanonicalKey: row.game_canonical_key,
});

export const collectManualCanonicalTitles = (
  sessions: AccountSessionWithMetadata[]
) => {
  const titles = sessions
    .filter((session) => session.platform === "manual")
    .map((session) => normalizeGameTitle(session.game));
  return Array.from(new Set(titles));
};

const stripSessionMetadata = (
  session: AccountSessionWithMetadata
): AccountSession => {
  const { gamePlatform, gameCanonicalKey, ...rest } = session;
  return rest;
};

const resolveMappedSource = (canonicalKey?: string | null) => {
  if (!canonicalKey) {
    return undefined;
  }
  return canonicalKey.startsWith("igdb:") ? "igdb" : "local";
};

export const applyManualMappings = (
  sessions: AccountSessionWithMetadata[],
  mappings: RawManualInputMappingRow[]
): AccountSession[] => {
  const hasManualSessions = sessions.some(
    (session) => session.platform === "manual"
  );
  if (!hasManualSessions) {
    return sessions.map(stripSessionMetadata);
  }

  const mappingIndex = new Map(
    mappings
      .filter((row) => row.mapped_game_id && row.mapped_title)
      .map((row) => [
        row.canonical_title,
        {
          title: row.mapped_title ?? "",
          platform: row.mapped_platform ?? "manual",
          capsuleUrl: row.mapped_capsule_url,
          canonicalKey: row.mapped_canonical_key,
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
          mappedSource: resolveMappedSource(session.gameCanonicalKey),
        });
      }
      return stripSessionMetadata(session);
    }

    return stripSessionMetadata({
      ...session,
      game: mapping.title,
      capsuleUrl: mapping.capsuleUrl ?? session.capsuleUrl,
      mappedPlatform: mapping.platform,
      mappedSource: resolveMappedSource(mapping.canonicalKey),
    });
  });
};

export type AccountStatsPeriod = {
  totalSeconds: number;
  sessionCount: number;
  topGameTitle: string | null;
  platforms: { platform: string; source: string | null; totalSeconds: number }[];
  topGames: { title: string; totalSeconds: number }[];
};

const buildStatsPeriod = (
  summaryRows: RawAccountPlaytimeSummaryRow[],
  platformRows: RawAccountPlatformTotalsRow[],
  topGameRows: RawAccountTopGameRow[]
): AccountStatsPeriod => {
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

export const buildAccountStatsPeriod = (
  summaryRows: RawAccountPlaytimeSummaryRow[],
  platformRows: RawAccountPlatformTotalsRow[],
  topGameRows: RawAccountTopGameRow[]
) => buildStatsPeriod(summaryRows, platformRows, topGameRows);

export const buildUserStatsPeriod = (
  summaryRows: RawAccountPlaytimeSummaryRow[],
  platformRows: RawAccountPlatformTotalsRow[],
  topGameRows: RawAccountTopGameRow[]
) => buildStatsPeriod(summaryRows, platformRows, topGameRows);
