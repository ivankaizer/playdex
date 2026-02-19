import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

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

type AccountRow = {
  id: number;
  platform: string;
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

type Sql = ReturnType<typeof postgres>;

export type RawAccountSessionRow = AccountSessionRow;
export type RawAccountSessionFilters = AccountSessionFilters;
export type RawManualInputMappingRow = ManualInputMappingRow;
export type RawAccountPlaytimeSummaryRow = AccountPlaytimeSummaryRow;
export type RawAccountPlatformTotalsRow = AccountPlatformTotalsRow;
export type RawAccountTopGameRow = AccountTopGameRow;

const mapCount = (rows: AccountSessionCountRow[]) => rows[0]?.total ?? 0;

const buildIgnoreShortSessionsFilter = (sql: Sql) => sql`
  and not (
    coalesce(us.ignore_short_sessions_minutes, 0) > 0
    and ps.ended_at is not null
    and extract(epoch from (ps.ended_at - ps.started_at)) <
      (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
  )
`;

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

const buildGameIdFilter = (sql: Sql, gameIds: unknown) => {
  const normalizedGameIds = normalizeGameIds(gameIds);
  if (normalizedGameIds.length === 1) {
    return sql`and ps.game_id = ${normalizedGameIds[0]}`;
  }
  if (normalizedGameIds.length > 1) {
    return sql`and ps.game_id = any(${sql.array(normalizedGameIds, 20)})`;
  }
  return sql``;
};

const listAccountSessions = async (
  accountId: number,
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
  const gameIdFilter = buildGameIdFilter(
    sql,
    filters.gameIds ?? filters.gameId
  );
  const fromFilter = filters.startedFrom
    ? sql`and ps.started_at >= ${filters.startedFrom}`
    : sql``;
  const toFilter = filters.startedTo
    ? sql`and ps.started_at < ${filters.startedTo}`
    : sql``;
  const sortKey = filters.sortKey ?? "date";
  const sortDirection = filters.sortDirection === "asc" ? sql`asc` : sql`desc`;
  const orderBy =
    sortKey === "duration"
      ? sql`extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))`
      : sql`ps.started_at`;

  return sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      g.id as game_id,
      pc_platform.key as platform,
      g.title,
      g.capsule_url,
      game_platform.key as game_platform,
      g.canonical_key as game_canonical_key,
      case
        when coalesce(us.ignore_short_sessions_minutes, 0) > 0
          and ps.ended_at is not null
          and extract(epoch from (ps.ended_at - ps.started_at)) <
            (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
          then true
        else false
      end as ignored
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      ${titleFilter}
      ${gameIdFilter}
      ${fromFilter}
      ${toFilter}
    order by ${orderBy} ${sortDirection}
    limit ${limit}
    offset ${offset}
  `;
};

const listUserSessions = async (
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
  const gameIdFilter = buildGameIdFilter(
    sql,
    filters.gameIds ?? filters.gameId
  );
  const fromFilter = filters.startedFrom
    ? sql`and ps.started_at >= ${filters.startedFrom}`
    : sql``;
  const toFilter = filters.startedTo
    ? sql`and ps.started_at < ${filters.startedTo}`
    : sql``;
  const sortKey = filters.sortKey ?? "date";
  const sortDirection = filters.sortDirection === "asc" ? sql`asc` : sql`desc`;
  const orderBy =
    sortKey === "duration"
      ? sql`extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))`
      : sql`ps.started_at`;

  return sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      g.id as game_id,
      pc_platform.key as platform,
      g.title,
      g.capsule_url,
      game_platform.key as game_platform,
      g.canonical_key as game_canonical_key,
      case
        when coalesce(us.ignore_short_sessions_minutes, 0) > 0
          and ps.ended_at is not null
          and extract(epoch from (ps.ended_at - ps.started_at)) <
            (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
          then true
        else false
      end as ignored
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    where pc.user_id = ${userId}
      ${titleFilter}
      ${gameIdFilter}
      ${fromFilter}
      ${toFilter}
    order by ${orderBy} ${sortDirection}
    limit ${limit}
    offset ${offset}
  `;
};

const countAccountSessions = async (
  accountId: number,
  userId: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
  const gameIdFilter = buildGameIdFilter(
    sql,
    filters.gameIds ?? filters.gameId
  );
  const fromFilter = filters.startedFrom
    ? sql`and ps.started_at >= ${filters.startedFrom}`
    : sql``;
  const toFilter = filters.startedTo
    ? sql`and ps.started_at < ${filters.startedTo}`
    : sql``;

  return sql<AccountSessionCountRow[]>`
    select count(*)::int as total
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      ${titleFilter}
      ${gameIdFilter}
      ${fromFilter}
      ${toFilter}
  `;
};

const countUserSessions = async (
  userId: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
  const gameIdFilter = buildGameIdFilter(
    sql,
    filters.gameIds ?? filters.gameId
  );
  const fromFilter = filters.startedFrom
    ? sql`and ps.started_at >= ${filters.startedFrom}`
    : sql``;
  const toFilter = filters.startedTo
    ? sql`and ps.started_at < ${filters.startedTo}`
    : sql``;

  return sql<AccountSessionCountRow[]>`
    select count(*)::int as total
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      ${titleFilter}
      ${gameIdFilter}
      ${fromFilter}
      ${toFilter}
  `;
};

const listAccountSessionsToday = async (
  accountId: number,
  userId: number,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      g.id as game_id,
      pc_platform.key as platform,
      g.title,
      g.capsule_url,
      game_platform.key as game_platform,
      g.canonical_key as game_canonical_key,
      case
        when coalesce(us.ignore_short_sessions_minutes, 0) > 0
          and ps.ended_at is not null
          and extract(epoch from (ps.ended_at - ps.started_at)) <
            (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
          then true
        else false
      end as ignored
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and (
        (ps.started_at >= (date_trunc('day', now() at time zone 'utc') at time zone 'utc')
          and ps.started_at < (date_trunc('day', now() at time zone 'utc') at time zone 'utc') + interval '1 day')
        or (ps.ended_at is not null
          and ps.ended_at >= (date_trunc('day', now() at time zone 'utc') at time zone 'utc')
          and ps.ended_at < (date_trunc('day', now() at time zone 'utc') at time zone 'utc') + interval '1 day')
      )
    order by ps.started_at desc
    limit ${limit}
  `;

const listUserSessionsToday = async (
  userId: number,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      g.id as game_id,
      pc_platform.key as platform,
      g.title,
      g.capsule_url,
      game_platform.key as game_platform,
      g.canonical_key as game_canonical_key,
      case
        when coalesce(us.ignore_short_sessions_minutes, 0) > 0
          and ps.ended_at is not null
          and extract(epoch from (ps.ended_at - ps.started_at)) <
            (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
          then true
        else false
      end as ignored
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    left join user_settings us on us.user_id = pc.user_id
    where pc.user_id = ${userId}
      and (
        (ps.started_at >= (date_trunc('day', now() at time zone 'utc') at time zone 'utc')
          and ps.started_at < (date_trunc('day', now() at time zone 'utc') at time zone 'utc') + interval '1 day')
        or (ps.ended_at is not null
          and ps.ended_at >= (date_trunc('day', now() at time zone 'utc') at time zone 'utc')
          and ps.ended_at < (date_trunc('day', now() at time zone 'utc') at time zone 'utc') + interval '1 day')
      )
    order by ps.started_at desc
    limit ${limit}
  `;

const loadAccountCurrentSession = async (
  accountId: number,
  userId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      g.id as game_id,
      pc_platform.key as platform,
      g.title,
      g.capsule_url,
      game_platform.key as game_platform,
      g.canonical_key as game_canonical_key,
      case
        when coalesce(us.ignore_short_sessions_minutes, 0) > 0
          and ps.ended_at is not null
          and extract(epoch from (ps.ended_at - ps.started_at)) <
            (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
          then true
        else false
      end as ignored
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    left join user_settings us on us.user_id = pc.user_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.ended_at is null
    order by ps.started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

const loadUserCurrentSession = async (userId: number, sql: Sql = getSql()) => {
  const rows = await sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      g.id as game_id,
      pc_platform.key as platform,
      g.title,
      g.capsule_url,
      game_platform.key as game_platform,
      g.canonical_key as game_canonical_key,
      case
        when coalesce(us.ignore_short_sessions_minutes, 0) > 0
          and ps.ended_at is not null
          and extract(epoch from (ps.ended_at - ps.started_at)) <
            (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
          then true
        else false
      end as ignored
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    left join user_settings us on us.user_id = pc.user_id
    where pc.user_id = ${userId}
      and ps.ended_at is null
    order by ps.started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

const deleteUserSession = async (
  sessionId: number,
  userId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    delete from play_sessions ps
    using platform_credentials pc
    where ps.platform_credential_id = pc.id
      and pc.user_id = ${userId}
      and ps.id = ${sessionId}
    returning ps.id
  `;
  return rows[0]?.id ?? null;
};

const loadManualInputMappings = async (
  userId: number,
  canonicalTitles: string[],
  sql: Sql = getSql()
) =>
  sql<ManualInputMappingRow[]>`
    select
      mgi.canonical_title,
      mgi.mapped_game_id,
      g.title as mapped_title,
      gp.key as mapped_platform,
      g.capsule_url as mapped_capsule_url,
      g.canonical_key as mapped_canonical_key
    from manual_game_inputs mgi
    left join games g on g.id = mgi.mapped_game_id
    left join platforms gp on gp.id = g.platform_id
    where mgi.user_id = ${userId}
      and mgi.canonical_title = any(${sql.array(canonicalTitles)})
  `;

const loadAccountPlaytimeSummary = async (
  accountId: number,
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlaytimeSummaryRow[]>`
    select
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds,
      count(*)::int as session_count
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
  `;

const loadUserPlaytimeSummary = async (
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlaytimeSummaryRow[]>`
    select
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds,
      count(*)::int as session_count
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
  `;

const loadUserPlaytimeSummaryForRange = async (
  userId: number,
  startedAt: Date,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlaytimeSummaryRow[]>`
    select
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds,
      count(*)::int as session_count
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      and ps.started_at < ${endedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
  `;

const loadAccountPlatformTotals = async (
  accountId: number,
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlatformTotalsRow[]>`
    select
      case
        when pc_platform.key = 'manual'
          then coalesce(mapped_platform.key, game_platform.key)
        else pc_platform.key
      end as platform,
      case
        when pc_platform.key = 'manual'
          and coalesce(mapped_game.canonical_key, g.canonical_key) like 'igdb:%'
          then 'igdb'
        when pc_platform.key = 'manual'
          and coalesce(mapped_game.id, g.id) is not null
          then 'local'
        else null
      end as source,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
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
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
    group by 1, 2
    order by 1, 2
  `;

const loadUserPlatformTotals = async (
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlatformTotalsRow[]>`
    select
      case
        when pc_platform.key = 'manual'
          then coalesce(mapped_platform.key, game_platform.key)
        else pc_platform.key
      end as platform,
      case
        when pc_platform.key = 'manual'
          and coalesce(mapped_game.canonical_key, g.canonical_key) like 'igdb:%'
          then 'igdb'
        when pc_platform.key = 'manual'
          and coalesce(mapped_game.id, g.id) is not null
          then 'local'
        else null
      end as source,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    left join manual_game_inputs mgi
      on pc_platform.key = 'manual'
      and mgi.user_id = pc.user_id
      and mgi.canonical_title = split_part(g.canonical_key, ':', 2)
    left join games mapped_game on mapped_game.id = mgi.mapped_game_id
    left join platforms mapped_platform on mapped_platform.id = mapped_game.platform_id
    left join user_settings us on us.user_id = pc.user_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
    group by 1, 2
    order by 1, 2
  `;

const loadUserPlatformTotalsForRange = async (
  userId: number,
  startedAt: Date,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlatformTotalsRow[]>`
    select
      case
        when pc_platform.key = 'manual'
          then coalesce(mapped_platform.key, game_platform.key)
        else pc_platform.key
      end as platform,
      case
        when pc_platform.key = 'manual'
          and coalesce(mapped_game.canonical_key, g.canonical_key) like 'igdb:%'
          then 'igdb'
        when pc_platform.key = 'manual'
          and coalesce(mapped_game.id, g.id) is not null
          then 'local'
        else null
      end as source,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    join platforms pc_platform on pc_platform.id = pc.platform_id
    join platforms game_platform on game_platform.id = g.platform_id
    left join manual_game_inputs mgi
      on pc_platform.key = 'manual'
      and mgi.user_id = pc.user_id
      and mgi.canonical_title = split_part(g.canonical_key, ':', 2)
    left join games mapped_game on mapped_game.id = mgi.mapped_game_id
    left join platforms mapped_platform on mapped_platform.id = mapped_game.platform_id
    left join user_settings us on us.user_id = pc.user_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      and ps.started_at < ${endedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
    group by 1, 2
    order by 1, 2
  `;

const loadAccountGameTotals = async (
  accountId: number,
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountTopGameRow[]>`
    select
      g.id as game_id,
      g.title,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
    group by g.id, g.title
    order by total_seconds desc
  `;

const loadUserGameTotals = async (
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountTopGameRow[]>`
    select
      g.id as game_id,
      g.title,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
    group by g.id, g.title
    order by total_seconds desc
  `;

const loadUserGameTotalsForRange = async (
  userId: number,
  startedAt: Date,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountTopGameRow[]>`
    select
      g.id as game_id,
      g.title,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    left join user_settings us on us.user_id = pc.user_id
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      and ps.started_at < ${endedAt}
      ${buildIgnoreShortSessionsFilter(sql)}
    group by g.id, g.title
    order by total_seconds desc
  `;

const listAccountsForUser = async (userId: number, sql: Sql = getSql()) =>
  sql<AccountRow[]>`
    select pc.id, p.key as platform
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where pc.user_id = ${userId}
    order by pc.id
  `;

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

  loadAccountGameTotals: (accountId: number, userId: number, startedAt: Date) =>
    loadAccountGameTotals(accountId, userId, startedAt),

  loadUserPlaytimeSummary: (userId: number, startedAt: Date) =>
    loadUserPlaytimeSummary(userId, startedAt),

  loadUserPlatformTotals: (userId: number, startedAt: Date) =>
    loadUserPlatformTotals(userId, startedAt),

  loadUserGameTotals: (userId: number, startedAt: Date) =>
    loadUserGameTotals(userId, startedAt),

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

  loadUserGameTotalsForRange: (
    userId: number,
    startedAt: Date,
    endedAt: Date
  ) => loadUserGameTotalsForRange(userId, startedAt, endedAt),
};
