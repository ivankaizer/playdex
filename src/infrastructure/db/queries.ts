import type postgres from "postgres";
import type { JSONValue } from "postgres";

import { getSql } from "./postgres";
import type { NormalizedGame } from "../../domain/normalize-game";

type Sql = ReturnType<typeof postgres>;

export type PlatformCredentialRow = {
  id: number;
  platform: string;
  api_key: string | null;
  account_id: string | null;
};

export type AccountRow = {
  id: number;
  platform: string;
};

export type AccountSessionRow = {
  id: number;
  started_at: Date;
  ended_at: Date | null;
  platform: string;
  title: string;
  capsule_url: string | null;
  game_platform: string;
  game_canonical_key: string;
};

export type AccountSessionCountRow = {
  total: number;
};

export type AccountSessionFilters = {
  title?: string | null;
  startedFrom?: Date | null;
  startedTo?: Date | null;
  sortKey?: "date" | "duration";
  sortDirection?: "asc" | "desc";
};

export type ProviderStatusRow = {
  id: number;
  platform: string;
  account_id: string | null;
  active: boolean;
  api_key: string | null;
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
  title: string;
  total_seconds: number;
};

export type ActiveSessionRow = {
  id: number;
  game_id: number;
};

export type UserAuthRow = {
  id: number;
  email: string;
  password: string | null;
  chat_id?: string | null;
};

export type NotificationTargetRow = {
  user_id: number;
  chat_id: string | null;
  platform: string;
};

export type GameRow = {
  id: number;
  title: string;
  platform: string;
};

export type UserChatRow = {
  id: number;
  email: string;
  chat_id: string | null;
};

export type ManualInputRow = {
  id: number;
  raw_title: string;
  canonical_title: string;
  mapped_game_id: number | null;
  mapped_title: string | null;
  mapped_platform: string | null;
  mapped_capsule_url: string | null;
};

export type ManualInputLookupRow = {
  id: number;
  raw_title: string;
  canonical_title: string;
  mapped_game_id: number | null;
};

export type GameSearchRow = {
  id: number;
  title: string;
  platform: string;
  capsule_url: string | null;
};

export const loadPlatformCredentials = async (sql: Sql = getSql()) =>
  sql<PlatformCredentialRow[]>`
    select id, platform, api_key, account_id
    from platform_credentials
    where active = true
  `;

export const loadPlatformCredentialsByApiKey = async (
  apiKey: string,
  platform: string,
  sql: Sql = getSql()
) =>
  sql<PlatformCredentialRow[]>`
    select id, platform, api_key, account_id
    from platform_credentials
    where api_key = ${apiKey}
      and platform = ${platform}
      and active = true
  `;

export const storeResolvedAccountId = async (
  credentialId: number,
  accountId: string,
  sql: Sql = getSql()
) => sql`
  update platform_credentials
  set account_id = ${accountId}, updated_at = now()
  where id = ${credentialId}
`;

export const healthCheck = async (sql: Sql = getSql()) => sql`select 1`;

export const listAccounts = async (sql: Sql = getSql()) =>
  sql<AccountRow[]>`
    select id, platform
    from platform_credentials
    order by id
  `;

export const listAccountsForUser = async (
  userId: number,
  sql: Sql = getSql()
) =>
  sql<AccountRow[]>`
    select id, platform
    from platform_credentials
    where user_id = ${userId}
    order by id
  `;

export const listAccountSessions = async (
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
  const fromFilter = filters.startedFrom
    ? sql`and ps.started_at >= ${filters.startedFrom}`
    : sql``;
  const toFilter = filters.startedTo
    ? sql`and ps.started_at < ${filters.startedTo}`
    : sql``;
  const sortKey = filters.sortKey ?? "date";
  const sortDirection =
    filters.sortDirection === "asc" ? sql`asc` : sql`desc`;
  const orderBy =
    sortKey === "duration"
      ? sql`extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))`
      : sql`ps.started_at`;

  return sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      pc.platform,
      g.title,
      g.capsule_url,
      g.platform as game_platform,
      g.canonical_key as game_canonical_key
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      ${titleFilter}
      ${fromFilter}
      ${toFilter}
    order by ${orderBy} ${sortDirection}
    limit ${limit}
    offset ${offset}
  `;
};

export const listUserSessions = async (
  userId: number,
  limit: number,
  offset: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
  const fromFilter = filters.startedFrom
    ? sql`and ps.started_at >= ${filters.startedFrom}`
    : sql``;
  const toFilter = filters.startedTo
    ? sql`and ps.started_at < ${filters.startedTo}`
    : sql``;
  const sortKey = filters.sortKey ?? "date";
  const sortDirection =
    filters.sortDirection === "asc" ? sql`asc` : sql`desc`;
  const orderBy =
    sortKey === "duration"
      ? sql`extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))`
      : sql`ps.started_at`;

  return sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      pc.platform,
      g.title,
      g.capsule_url,
      g.platform as game_platform,
      g.canonical_key as game_canonical_key
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      ${titleFilter}
      ${fromFilter}
      ${toFilter}
    order by ${orderBy} ${sortDirection}
    limit ${limit}
    offset ${offset}
  `;
};

export const countAccountSessions = async (
  accountId: number,
  userId: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
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
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      ${titleFilter}
      ${fromFilter}
      ${toFilter}
  `;
};

export const countUserSessions = async (
  userId: number,
  filters: AccountSessionFilters = {},
  sql: Sql = getSql()
) => {
  const titleFilter = filters.title
    ? sql`and g.title ilike ${`%${filters.title}%`}`
    : sql``;
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
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      ${titleFilter}
      ${fromFilter}
      ${toFilter}
  `;
};

export const listAccountSessionsToday = async (
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
      pc.platform,
      g.title,
      g.capsule_url,
      g.platform as game_platform,
      g.canonical_key as game_canonical_key
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
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

export const listUserSessionsToday = async (
  userId: number,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      pc.platform,
      g.title,
      g.capsule_url,
      g.platform as game_platform,
      g.canonical_key as game_canonical_key
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
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

export const loadAccountCurrentSession = async (
  accountId: number,
  userId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      pc.platform,
      g.title,
      g.capsule_url,
      g.platform as game_platform,
      g.canonical_key as game_canonical_key
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.ended_at is null
    order by ps.started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

export const loadUserCurrentSession = async (
  userId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      pc.platform,
      g.title,
      g.capsule_url
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      and ps.ended_at is null
    order by ps.started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

export const loadAccountPlaytimeSummary = async (
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
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
  `;

export const loadUserPlaytimeSummary = async (
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
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
  `;

export const loadAccountPlatformTotals = async (
  accountId: number,
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlatformTotalsRow[]>`
    select
      case
        when pc.platform = 'manual'
          then coalesce(mapped_game.platform, g.platform)
        else pc.platform
      end as platform,
      case
        when pc.platform = 'manual'
          and coalesce(mapped_game.canonical_key, g.canonical_key) like 'igdb:%'
          then 'igdb'
        when pc.platform = 'manual'
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
    left join manual_game_inputs mgi
      on pc.platform = 'manual'
      and mgi.user_id = pc.user_id
      and mgi.canonical_title = split_part(g.canonical_key, ':', 2)
    left join games mapped_game on mapped_game.id = mgi.mapped_game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
    group by 1, 2
    order by 1, 2
  `;

export const loadUserPlatformTotals = async (
  userId: number,
  startedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlatformTotalsRow[]>`
    select
      case
        when pc.platform = 'manual'
          then coalesce(mapped_game.platform, g.platform)
        else pc.platform
      end as platform,
      case
        when pc.platform = 'manual'
          and coalesce(mapped_game.canonical_key, g.canonical_key) like 'igdb:%'
          then 'igdb'
        when pc.platform = 'manual'
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
    left join manual_game_inputs mgi
      on pc.platform = 'manual'
      and mgi.user_id = pc.user_id
      and mgi.canonical_title = split_part(g.canonical_key, ':', 2)
    left join games mapped_game on mapped_game.id = mgi.mapped_game_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
    group by 1, 2
    order by 1, 2
  `;

export const loadUserPlatformTotalsForRange = async (
  userId: number,
  startedAt: Date,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<AccountPlatformTotalsRow[]>`
    select
      case
        when pc.platform = 'manual'
          then coalesce(mapped_game.platform, g.platform)
        else pc.platform
      end as platform,
      case
        when pc.platform = 'manual'
          and coalesce(mapped_game.canonical_key, g.canonical_key) like 'igdb:%'
          then 'igdb'
        when pc.platform = 'manual'
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
    left join manual_game_inputs mgi
      on pc.platform = 'manual'
      and mgi.user_id = pc.user_id
      and mgi.canonical_title = split_part(g.canonical_key, ':', 2)
    left join games mapped_game on mapped_game.id = mgi.mapped_game_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
      and ps.started_at < ${endedAt}
    group by 1, 2
    order by 1, 2
  `;

export const loadAccountTopGames = async (
  accountId: number,
  userId: number,
  startedAt: Date,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<AccountTopGameRow[]>`
    select
      g.title,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
      and pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
    group by g.title
    order by total_seconds desc
    limit ${limit}
  `;

export const loadUserTopGames = async (
  userId: number,
  startedAt: Date,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<AccountTopGameRow[]>`
    select
      g.title,
      coalesce(
        sum(extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))),
        0
      )::float as total_seconds
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where pc.user_id = ${userId}
      and ps.started_at >= ${startedAt}
    group by g.title
    order by total_seconds desc
    limit ${limit}
  `;

export const listProvidersForUser = async (
  userId: number,
  sql: Sql = getSql()
) =>
  sql<ProviderStatusRow[]>`
    select id, platform, account_id, active, api_key
    from platform_credentials
    where user_id = ${userId}
    order by id
  `;

export const loadProviderByPlatform = async (
  userId: number,
  platform: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ProviderStatusRow[]>`
    select id, platform, account_id, active, api_key
    from platform_credentials
    where user_id = ${userId}
      and platform = ${platform}
    limit 1
  `;
  return rows[0] ?? null;
};

export const updateProviderApiKey = async (
  credentialId: number,
  apiKey: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ProviderStatusRow[]>`
    update platform_credentials
    set api_key = ${apiKey}, updated_at = now()
    where id = ${credentialId}
    returning id, platform, account_id, active, api_key
  `;
  return rows[0] ?? null;
};

export const createPlayniteCredential = async (
  userId: number,
  apiKey: string,
  sql: Sql = getSql()
) =>
  sql<ProviderStatusRow[]>`
    insert into platform_credentials (user_id, platform, api_key, account_id, active)
    values (${userId}, 'playnite', ${apiKey}, 'local', true)
    returning id, platform, account_id, active, api_key
  `;

export const createManualCredential = async (
  userId: number,
  sql: Sql = getSql()
) =>
  sql<ProviderStatusRow[]>`
    insert into platform_credentials (user_id, platform, api_key, account_id, active)
    values (${userId}, 'manual', 'manual', 'manual', true)
    returning id, platform, account_id, active, api_key
  `;

export const loadUserByEmail = async (
  email: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<UserAuthRow[]>`
    select id, email, password, chat_id
    from users
    where email = ${email}
    limit 1
  `;
  return rows[0] ?? null;
};

export const loadUserByChatId = async (
  chatId: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<UserChatRow[]>`
    select id, email, chat_id
    from users
    where chat_id = ${chatId}
    limit 1
  `;
  return rows[0] ?? null;
};

export const loadNotificationTargetByCredential = async (
  platformCredentialId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<NotificationTargetRow[]>`
    select u.id as user_id, u.chat_id, pc.platform
    from platform_credentials pc
    join users u on u.id = pc.user_id
    where pc.id = ${platformCredentialId}
    limit 1
  `;
  return rows[0] ?? null;
};

export const loadGameById = async (gameId: number, sql: Sql = getSql()) => {
  const rows = await sql<GameRow[]>`
    select id, title, platform
    from games
    where id = ${gameId}
    limit 1
  `;
  return rows[0] ?? null;
};

export const listManualInputs = async (
  userId: number,
  sql: Sql = getSql()
) =>
  sql<ManualInputRow[]>`
    select
      mgi.id,
      mgi.raw_title,
      mgi.canonical_title,
      mgi.mapped_game_id,
      g.title as mapped_title,
      g.platform as mapped_platform,
      g.capsule_url as mapped_capsule_url
    from manual_game_inputs mgi
    left join games g on g.id = mgi.mapped_game_id
    where mgi.user_id = ${userId}
    order by mgi.updated_at desc
  `;

export type ManualInputMappingRow = {
  canonical_title: string;
  mapped_game_id: number | null;
  mapped_title: string | null;
  mapped_platform: string | null;
  mapped_capsule_url: string | null;
  mapped_canonical_key: string | null;
};

export const loadManualInputMappings = async (
  userId: number,
  canonicalTitles: string[],
  sql: Sql = getSql()
) =>
  sql<ManualInputMappingRow[]>`
    select
      mgi.canonical_title,
      mgi.mapped_game_id,
      g.title as mapped_title,
      g.platform as mapped_platform,
      g.capsule_url as mapped_capsule_url,
      g.canonical_key as mapped_canonical_key
    from manual_game_inputs mgi
    left join games g on g.id = mgi.mapped_game_id
    where mgi.user_id = ${userId}
      and mgi.canonical_title = any(${sql.array(canonicalTitles)})
  `;

export const loadManualInputByCanonical = async (
  userId: number,
  canonicalTitle: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ManualInputLookupRow[]>`
    select id, raw_title, canonical_title, mapped_game_id
    from manual_game_inputs
    where user_id = ${userId}
      and canonical_title = ${canonicalTitle}
    limit 1
  `;
  return rows[0] ?? null;
};

export const insertManualInput = async (
  userId: number,
  rawTitle: string,
  canonicalTitle: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ManualInputLookupRow[]>`
    insert into manual_game_inputs (user_id, raw_title, canonical_title)
    values (${userId}, ${rawTitle}, ${canonicalTitle})
    returning id, raw_title, canonical_title, mapped_game_id
  `;
  return rows[0] ?? null;
};

export const updateManualInputTitle = async (
  inputId: number,
  rawTitle: string,
  sql: Sql = getSql()
) => sql`
  update manual_game_inputs
  set raw_title = ${rawTitle}, updated_at = now()
  where id = ${inputId}
`;

export const updateManualInputMapping = async (
  userId: number,
  inputId: number,
  gameId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<ManualInputLookupRow[]>`
    update manual_game_inputs
    set mapped_game_id = ${gameId}, updated_at = now()
    where id = ${inputId}
      and user_id = ${userId}
    returning id, raw_title, canonical_title, mapped_game_id
  `;
  return rows[0] ?? null;
};

export const searchGamesByTitle = async (
  title: string,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<GameSearchRow[]>`
    select id, title, platform, capsule_url
    from games
    where title ilike ${`%${title}%`}
    order by updated_at desc
    limit ${limit}
  `;

export const ensureIgdbGame = async (
  game: {
    igdbId: string;
    title: string;
    platform: string | null;
    capsuleUrl: string | null;
  },
  sql: Sql = getSql()
) => {
  const canonicalKey = `igdb:${game.igdbId}`;
  const rows = await sql<{ id: number }[]>`
    insert into games (platform, title, external_id, canonical_key, capsule_url)
    values (
      ${game.platform ?? "igdb"},
      ${game.title},
      ${game.igdbId},
      ${canonicalKey},
      ${game.capsuleUrl}
    )
    on conflict (canonical_key)
    do update set
      title = excluded.title,
      platform = excluded.platform,
      external_id = excluded.external_id,
      capsule_url = coalesce(excluded.capsule_url, games.capsule_url),
      updated_at = now()
    returning id
  `;
  return rows[0].id;
};

export const endActiveSessionsForUser = async (
  userId: number,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<{ id: number; platform: string; title: string }[]>`
    update play_sessions as ps
    set ended_at = ${endedAt}, updated_at = now()
    from platform_credentials pc, games g
    where ps.platform_credential_id = pc.id
      and g.id = ps.game_id
      and pc.user_id = ${userId}
      and ps.ended_at is null
    returning ps.id, pc.platform, g.title
  `;

export const endActiveSessionsForCredential = async (
  platformCredentialId: number,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<{ id: number; game_id: number }[]>`
    update play_sessions
    set ended_at = ${endedAt}, updated_at = now()
    where platform_credential_id = ${platformCredentialId}
      and ended_at is null
    returning id, game_id
  `;

export const ensureGameId = async (
  game: NormalizedGame,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    insert into games (platform, title, external_id, canonical_key, capsule_url)
    values (
      ${game.platform},
      ${game.title},
      ${game.externalId ?? null},
      ${game.canonicalKey},
      ${game.capsuleUrl ?? null}
    )
    on conflict (canonical_key)
    do update set
      title = excluded.title,
      external_id = excluded.external_id,
      capsule_url = coalesce(excluded.capsule_url, games.capsule_url),
      updated_at = now()
    returning id
  `;
  return rows[0].id;
};

export const recordPollResult = async (
  platformCredentialId: number,
  gameId: number | null,
  polledAt: Date,
  rawPayload: unknown,
  sql: Sql = getSql()
) => {
  const payload =
    rawPayload === undefined ? null : sql.json(rawPayload as JSONValue);
  await sql`
    insert into poll_results (platform_credential_id, game_id, polled_at, raw_payload)
    values (${platformCredentialId}, ${gameId}, ${polledAt}, ${payload})
  `;
};

export const fetchActivePlaySession = async (
  platformCredentialId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<ActiveSessionRow[]>`
    select id, game_id
    from play_sessions
    where platform_credential_id = ${platformCredentialId}
      and ended_at is null
    order by started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

export const endPlaySession = async (
  sessionId: number,
  endedAt: Date,
  sql: Sql = getSql()
) => sql`
  update play_sessions
  set ended_at = ${endedAt}, updated_at = now()
  where id = ${sessionId}
`;

export const startPlaySession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  sql: Sql = getSql()
) => sql`
  insert into play_sessions (platform_credential_id, game_id, started_at)
  values (${platformCredentialId}, ${gameId}, ${startedAt})
`;

export const insertActivePlaySession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    insert into play_sessions (platform_credential_id, game_id, started_at)
    values (${platformCredentialId}, ${gameId}, ${startedAt})
    returning id
  `;
  return rows[0]?.id ?? null;
};

export const insertPlaySession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  endedAt: Date,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
    values (${platformCredentialId}, ${gameId}, ${startedAt}, ${endedAt})
    returning id
  `;
  return rows[0]?.id ?? null;
};

export const ensureMigrationsTable = async (sql: Sql = getSql()) => sql`
  create table if not exists schema_migrations (
    version text primary key,
    applied_at timestamptz not null default now()
  )
`;

export const fetchAppliedMigrations = async (sql: Sql = getSql()) =>
  sql<{ version: string }[]>`
    select version from schema_migrations
  `;

export const insertAppliedMigration = async (
  filename: string,
  sql: Sql = getSql()
) => sql`
  insert into schema_migrations (version) values (${filename})
`;
