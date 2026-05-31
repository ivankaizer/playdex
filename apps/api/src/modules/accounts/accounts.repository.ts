import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import {
  durationSortExpression,
  ignoredCaseExpression,
  ignoreShortSessionsFilter,
  sessionCountExpression,
  totalSecondsExpression,
  utcDayEndExpression,
  utcDayStartExpression,
} from "../../common/db/expressions";

export type AccountSessionFilters = {
  title?: string | null;
  gameId?: number | null;
  gameIds?: number[] | null;
  startedFrom?: Date | null;
  startedTo?: Date | null;
  sortKey?: "date" | "duration";
  sortDirection?: "asc" | "desc";
};

export type AccountSessionRow = {
  id: number;
  started_at: Date;
  ended_at: Date | null;
  game_id: number;
  platform: string;
  title: string;
  capsule_url: string | null;
  game_platform: string;
  game_canonical_key: string;
  ignored: boolean;
};

type AccountSessionCountRow = {
  total: number;
};

export type AccountPlaytimeSummaryRow = {
  total_seconds: number;
  session_count: number;
};

export type AccountPlatformTotalsRow = {
  platform: string;
  total_seconds: number;
  source: string | null;
};

export type AccountTopGameRow = {
  game_id: number;
  title: string;
  total_seconds: number;
};

export type ManualInputMappingRow = {
  canonical_title: string;
  mapped_game_id: number | null;
  mapped_title: string | null;
  mapped_platform: string | null;
  mapped_capsule_url: string | null;
  mapped_canonical_key: string | null;
};

export type RawAccountSessionRow = AccountSessionRow;
export type RawAccountSessionFilters = AccountSessionFilters;
export type RawManualInputMappingRow = ManualInputMappingRow;
export type RawAccountPlaytimeSummaryRow = AccountPlaytimeSummaryRow;
export type RawAccountPlatformTotalsRow = AccountPlatformTotalsRow;
export type RawAccountTopGameRow = AccountTopGameRow;

const baseSessionSelects = [
  "ps.id",
  "ps.started_at",
  "ps.ended_at",
  "g.id as game_id",
  "pc_platform.key as platform",
  "g.title",
  "g.capsule_url",
  "game_platform.key as game_platform",
  "g.canonical_key as game_canonical_key",
  ignoredCaseExpression().as("ignored"),
] as const;

const mapCount = (rows: AccountSessionCountRow[]) => rows[0]?.total ?? 0;

const normalizeGameIds = (value: unknown) => {
  if (Array.isArray(value)) {
    return value
      .map((id) => Number(id))
      .filter((id) => Number.isInteger(id) && id > 0);
  }
  if (typeof value === "number") {
    if (Number.isInteger(value) && value > 0) {
      return [value];
    }
    return [] as number[];
  }
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (!trimmed) {
      return [] as number[];
    }
    if (trimmed.includes(",")) {
      return trimmed
        .split(",")
        .map((entry) => Number(entry.trim()))
        .filter((id) => Number.isInteger(id) && id > 0);
    }
    const numeric = Number(trimmed);
    if (Number.isInteger(numeric) && numeric > 0) {
      return [numeric];
    }
  }
  return [] as number[];
};

// eslint-disable-next-line @typescript-eslint/no-explicit-any
const mapSessionRows = (rows: any[]): RawAccountSessionRow[] =>
  rows.map((row) => ({
    ...row,
    id: Number(row.id),
    game_id: Number(row.game_id),
    ignored: Boolean(row.ignored),
  }));

const listSessions = async (
  accountId: number | undefined,
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {},
  db: DbExecutor = getDb()
): Promise<RawAccountSessionRow[]> => {
  const sortKey = filters.sortKey ?? "date";
  const sortDirection = filters.sortDirection ?? "desc";
  const orderByRef =
    sortKey === "duration" ? durationSortExpression() : sql`ps.started_at`;

  const resolvedGameIds = normalizeGameIds(filters.gameIds ?? filters.gameId);

  let query = db
    .selectFrom("play_sessions as ps")
    .innerJoin(
      "platform_credentials as pc",
      "pc.id",
      "ps.platform_credential_id"
    )
    .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
    .innerJoin("games as g", "g.id", "ps.game_id")
    .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
    .innerJoin(
      "platforms as game_platform",
      "game_platform.id",
      "g.platform_id"
    )
    .$castTo<{
      id: string;
      started_at: Date;
      ended_at: Date | null;
      game_id: string;
      platform: string;
      title: string;
      capsule_url: string | null;
      game_platform: string;
      game_canonical_key: string;
      ignored: unknown;
    }>()
    .where("pc.user_id", "=", String(userId));

  if (accountId !== undefined) {
    query = query.where("ps.platform_credential_id", "=", String(accountId));
  }

  if (filters.title) {
    query = query.where("g.title", "ilike", `%${filters.title}%`);
  }

  if (resolvedGameIds.length === 1) {
    query = query.where("ps.game_id", "=", String(resolvedGameIds[0]));
  } else if (resolvedGameIds.length > 1) {
    query = query.where("ps.game_id", "in", resolvedGameIds.map(String));
  }

  if (filters.startedFrom) {
    query = query.where("ps.started_at", ">=", filters.startedFrom);
  }

  if (filters.startedTo) {
    query = query.where("ps.started_at", "<", filters.startedTo);
  }

  const rows = await query
    .select(baseSessionSelects)
    .orderBy(orderByRef, sortDirection)
    .limit(limit)
    .offset(offset)
    .execute();

  return mapSessionRows(rows);
};

const countSessions = async (
  accountId: number | undefined,
  userId: number,
  filters: AccountSessionFilters = {},
  db: DbExecutor = getDb()
): Promise<AccountSessionCountRow[]> => {
  const resolvedGameIds = normalizeGameIds(filters.gameIds ?? filters.gameId);

  let query = db
    .selectFrom("play_sessions as ps")
    .innerJoin(
      "platform_credentials as pc",
      "pc.id",
      "ps.platform_credential_id"
    )
    .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
    .innerJoin("games as g", "g.id", "ps.game_id")
    .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
    .innerJoin(
      "platforms as game_platform",
      "game_platform.id",
      "g.platform_id"
    )
    .$castTo<{ total: number }>()
    .where("pc.user_id", "=", String(userId));

  if (accountId !== undefined) {
    query = query.where("ps.platform_credential_id", "=", String(accountId));
  }

  if (filters.title) {
    query = query.where("g.title", "ilike", `%${filters.title}%`);
  }

  if (resolvedGameIds.length === 1) {
    query = query.where("ps.game_id", "=", String(resolvedGameIds[0]));
  } else if (resolvedGameIds.length > 1) {
    query = query.where("ps.game_id", "in", resolvedGameIds.map(String));
  }

  if (filters.startedFrom) {
    query = query.where("ps.started_at", ">=", filters.startedFrom);
  }

  if (filters.startedTo) {
    query = query.where("ps.started_at", "<", filters.startedTo);
  }

  const rows = await query
    .select(sessionCountExpression().as("total"))
    .execute();

  return rows as unknown as AccountSessionCountRow[];
};

const listSessionsToday = async (
  accountId: number | undefined,
  userId: number,
  limit: number,
  db: DbExecutor = getDb()
): Promise<RawAccountSessionRow[]> => {
  let query = db
    .selectFrom("play_sessions as ps")
    .innerJoin(
      "platform_credentials as pc",
      "pc.id",
      "ps.platform_credential_id"
    )
    .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
    .innerJoin("games as g", "g.id", "ps.game_id")
    .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
    .innerJoin(
      "platforms as game_platform",
      "game_platform.id",
      "g.platform_id"
    )
    .$castTo<{
      id: string;
      started_at: Date;
      ended_at: Date | null;
      game_id: string;
      platform: string;
      title: string;
      capsule_url: string | null;
      game_platform: string;
      game_canonical_key: string;
      ignored: unknown;
    }>()
    .where("pc.user_id", "=", String(userId))
    .where((eb) =>
      eb.or([
        eb.and([
          eb("ps.started_at", ">=", utcDayStartExpression()),
          eb("ps.started_at", "<", utcDayEndExpression()),
        ]),
        eb.and([
          eb("ps.ended_at", "is not", null),
          eb("ps.ended_at", ">=", utcDayStartExpression()),
          eb("ps.ended_at", "<", utcDayEndExpression()),
        ]),
      ])
    );

  if (accountId !== undefined) {
    query = query.where("ps.platform_credential_id", "=", String(accountId));
  }

  const rows = await query
    .select(baseSessionSelects)
    .orderBy("ps.started_at", "desc")
    .limit(limit)
    .execute();

  return mapSessionRows(rows);
};

const loadCurrentSession = async (
  accountId: number | undefined,
  userId: number,
  db: DbExecutor = getDb()
): Promise<RawAccountSessionRow | null> => {
  let query = db
    .selectFrom("play_sessions as ps")
    .innerJoin(
      "platform_credentials as pc",
      "pc.id",
      "ps.platform_credential_id"
    )
    .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
    .innerJoin("games as g", "g.id", "ps.game_id")
    .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
    .innerJoin(
      "platforms as game_platform",
      "game_platform.id",
      "g.platform_id"
    )
    .$castTo<{
      id: string;
      started_at: Date;
      ended_at: Date | null;
      game_id: string;
      platform: string;
      title: string;
      capsule_url: string | null;
      game_platform: string;
      game_canonical_key: string;
      ignored: unknown;
    }>()
    .where("pc.user_id", "=", String(userId))
    .where("ps.ended_at", "is", null);

  if (accountId !== undefined) {
    query = query.where("ps.platform_credential_id", "=", String(accountId));
  }

  const rows = await query
    .select(baseSessionSelects)
    .orderBy("ps.started_at", "desc")
    .limit(1)
    .execute();

  if (rows.length === 0) {
    return null;
  }

  return mapSessionRows(rows)[0];
};

const deleteUserSession = async (
  sessionId: number,
  userId: number,
  db: DbExecutor = getDb()
) => {
  // Kysely does not support DELETE ... USING, so use a sql fragment for the
  // join-condition delete where the user must own the session's credential.
  const result = await sql<{ id: string }>`
    delete from play_sessions ps
    using platform_credentials pc
    where ps.platform_credential_id = pc.id
      and pc.user_id = ${userId}
      and ps.id = ${sessionId}
    returning ps.id
  `.execute(db);

  const id = result.rows[0]?.id;
  return id ? Number(id) : null;
};

const loadManualInputMappings = async (
  userId: number,
  canonicalTitles: string[],
  db: DbExecutor = getDb()
): Promise<ManualInputMappingRow[]> => {
  if (canonicalTitles.length === 0) {
    return [];
  }

  const rows = await db
    .selectFrom("manual_game_inputs as mgi")
    .leftJoin("games as g", "g.id", "mgi.mapped_game_id")
    .leftJoin("platforms as gp", "gp.id", "g.platform_id")
    .select([
      "mgi.canonical_title",
      "mgi.mapped_game_id",
      "g.title as mapped_title",
      "gp.key as mapped_platform",
      "g.capsule_url as mapped_capsule_url",
      "g.canonical_key as mapped_canonical_key",
    ])
    .where("mgi.user_id", "=", String(userId))
    .where("mgi.canonical_title", "in", canonicalTitles)
    .execute();

  return rows.map((row) => ({
    ...row,
    mapped_game_id: row.mapped_game_id ? Number(row.mapped_game_id) : null,
  })) as ManualInputMappingRow[];
};

const loadPlaytimeSummary = async (
  accountId: number | undefined,
  userId: number,
  startedAt: Date,
  endedAt: Date | undefined,
  db: DbExecutor = getDb()
): Promise<AccountPlaytimeSummaryRow[]> => {
  let query = db
    .selectFrom("play_sessions as ps")
    .innerJoin(
      "platform_credentials as pc",
      "pc.id",
      "ps.platform_credential_id"
    )
    .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
    .innerJoin("games as g", "g.id", "ps.game_id")
    .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
    .innerJoin(
      "platforms as game_platform",
      "game_platform.id",
      "g.platform_id"
    )
    .$castTo<AccountPlaytimeSummaryRow>()
    .where("pc.user_id", "=", String(userId))
    .where("ps.started_at", ">=", startedAt)
    .where(ignoreShortSessionsFilter());

  if (accountId !== undefined) {
    query = query.where("ps.platform_credential_id", "=", String(accountId));
  }

  if (endedAt) {
    query = query.where("ps.started_at", "<", endedAt);
  }

  const rows = await query
    .select([
      totalSecondsExpression().as("total_seconds"),
      sessionCountExpression().as("session_count"),
    ])
    .execute();

  return rows as unknown as AccountPlaytimeSummaryRow[];
};

const loadPlatformTotals = async (
  accountId: number | undefined,
  userId: number,
  startedAt: Date,
  endedAt: Date | undefined,
  db: DbExecutor = getDb()
): Promise<AccountPlatformTotalsRow[]> => {
  // PostgreSQL split_part in manual-mapping join has no clear typed-builder equivalent.
  const platformCase = sql<string>`case
    when ${sql.ref("pc_platform.key")} = 'manual'
      then coalesce(${sql.ref("mapped_platform.key")}, ${sql.ref("game_platform.key")})
    else ${sql.ref("pc_platform.key")}
  end`;

  const result = await sql<AccountPlatformTotalsRow>`
    select
      ${platformCase} as platform,
      null as source,
      ${totalSecondsExpression()} as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    left join manual_game_inputs mgi
      on pc_platform.key = 'manual'
      and mgi.user_id = pc.user_id
      and mgi.canonical_title = split_part(g.canonical_key, ':', 2)
    left join games mapped_game on mapped_game.id = mgi.mapped_game_id
    left join platforms mapped_platform on mapped_platform.id = mapped_game.platform_id
    where ${
      accountId !== undefined
        ? sql`ps.platform_credential_id = ${accountId} and`
        : sql``
    }
      pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${endedAt ? sql`and ps.started_at < ${endedAt}` : sql``}
      and ${ignoreShortSessionsFilter()}
    group by 1, 2
    order by 1, 2
  `.execute(db);

  return result.rows;
};

const loadGameTotals = async (
  accountId: number | undefined,
  userId: number,
  startedAt: Date,
  endedAt: Date | undefined,
  db: DbExecutor = getDb()
): Promise<AccountTopGameRow[]> => {
  let query = db
    .selectFrom("play_sessions as ps")
    .innerJoin(
      "platform_credentials as pc",
      "pc.id",
      "ps.platform_credential_id"
    )
    .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
    .innerJoin("games as g", "g.id", "ps.game_id")
    .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
    .innerJoin(
      "platforms as game_platform",
      "game_platform.id",
      "g.platform_id"
    )
    .$castTo<{ game_id: string; title: string; total_seconds: number }>()
    .where("pc.user_id", "=", String(userId))
    .where("ps.started_at", ">=", startedAt)
    .where(ignoreShortSessionsFilter());

  if (accountId !== undefined) {
    query = query.where("ps.platform_credential_id", "=", String(accountId));
  }

  if (endedAt) {
    query = query.where("ps.started_at", "<", endedAt);
  }

  const rows = await query
    .select([
      "g.id as game_id",
      "g.title",
      totalSecondsExpression().as("total_seconds"),
    ])
    .groupBy(["g.id", "g.title"])
    .orderBy("total_seconds", "desc")
    .execute();

  return rows.map((row) => ({
    ...row,
    game_id: Number(row.game_id),
  })) as unknown as AccountTopGameRow[];
};

const listAccountsForUser = async (userId: number, db: DbExecutor = getDb()) =>
  db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select(["pc.id", "p.key as platform"])
    .where("pc.user_id", "=", String(userId))
    .orderBy("pc.id")
    .execute();

export const accountsRepository = {
  listAccounts: (userId: number) => listAccountsForUser(userId),

  listAccountSessions: (
    accountId: number,
    userId: number,
    limit: number,
    offset: number,
    filters: RawAccountSessionFilters
  ) => listSessions(accountId, userId, limit, offset, filters),

  listUserSessions: (
    userId: number,
    limit: number,
    offset: number,
    filters: RawAccountSessionFilters
  ) => listSessions(undefined, userId, limit, offset, filters),

  countAccountSessions: (
    accountId: number,
    userId: number,
    filters: RawAccountSessionFilters
  ) => countSessions(accountId, userId, filters).then((rows) => mapCount(rows)),

  countUserSessions: (userId: number, filters: RawAccountSessionFilters) =>
    countSessions(undefined, userId, filters).then((rows) => mapCount(rows)),

  listAccountSessionsToday: (
    accountId: number,
    userId: number,
    limit: number
  ) => listSessionsToday(accountId, userId, limit),

  listUserSessionsToday: (userId: number, limit: number) =>
    listSessionsToday(undefined, userId, limit),

  getAccountCurrentSession: (accountId: number, userId: number) =>
    loadCurrentSession(accountId, userId),

  getUserCurrentSession: (userId: number) =>
    loadCurrentSession(undefined, userId),

  deleteUserSession: (sessionId: number, userId: number) =>
    deleteUserSession(sessionId, userId),

  loadManualMappings: (userId: number, canonicalTitles: string[]) =>
    loadManualInputMappings(userId, canonicalTitles),

  loadAccountPlaytimeSummary: (
    accountId: number,
    userId: number,
    startedAt: Date
  ) => loadPlaytimeSummary(accountId, userId, startedAt, undefined),

  loadAccountPlatformTotals: (
    accountId: number,
    userId: number,
    startedAt: Date
  ) => loadPlatformTotals(accountId, userId, startedAt, undefined),

  loadAccountGameTotals: (accountId: number, userId: number, startedAt: Date) =>
    loadGameTotals(accountId, userId, startedAt, undefined),

  loadUserPlaytimeSummary: (userId: number, startedAt: Date) =>
    loadPlaytimeSummary(undefined, userId, startedAt, undefined),

  loadUserPlatformTotals: (userId: number, startedAt: Date) =>
    loadPlatformTotals(undefined, userId, startedAt, undefined),

  loadUserGameTotals: (userId: number, startedAt: Date) =>
    loadGameTotals(undefined, userId, startedAt, undefined),

  loadUserPlaytimeSummaryForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ) => loadPlaytimeSummary(undefined, userId, startedAt, endedAt),

  loadUserPlatformTotalsForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ) => loadPlatformTotals(undefined, userId, startedAt, endedAt),

  loadUserGameTotalsForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ) => loadGameTotals(undefined, userId, startedAt, endedAt),
};
