import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

type Sql = ReturnType<typeof postgres>;

type AdminRecentSessionRow = {
  user_id: number;
  email: string;
  session_id: number;
  game_name: string;
  started_at: Date;
  duration_seconds: number;
  live: boolean;
};

type AdminMovableSessionRow = {
  session_id: number;
  source_credential_id: number;
  user_email: string;
  platform: string;
  account_user: string | null;
  game_name: string;
  started_at: Date;
  ended_at: Date | null;
  duration_seconds: number;
  live: boolean;
};

type AdminPlatformStatusRow = {
  platform: string;
  total_credentials: number;
  active_credentials: number;
};

type AdminCredentialRow = {
  id: number;
  user_email: string;
  platform: string;
  api_key: string | null;
  account_id: string | null;
  active: boolean;
};

type AdminGameRow = {
  id: number;
  title: string;
  title_locked: boolean;
  platform: string;
  capsule_url: string | null;
  capsule_url_locked: boolean;
};

const listRecentSessions = async (sql: Sql = getSql()) =>
  sql<AdminRecentSessionRow[]>`
    with ranked as (
      select
        u.id as user_id,
        u.email,
        ps.id as session_id,
        g.title as game_name,
        ps.started_at,
        greatest(
          0,
          floor(
            extract(
              epoch
              from (coalesce(ps.ended_at, now()) - ps.started_at)
            )
          )
        )::integer as duration_seconds,
        (ps.ended_at is null) as live,
        row_number() over (
          partition by u.id
          order by ps.started_at desc, ps.id desc
        ) as session_rank
      from play_sessions ps
      join platform_credentials pc on pc.id = ps.platform_credential_id
      join users u on u.id = pc.user_id
      join games g on g.id = ps.game_id
    )
    select
      user_id,
      email,
      session_id,
      game_name,
      started_at,
      duration_seconds,
      live
    from ranked
    where session_rank <= 3
    order by email asc, started_at desc, session_id desc
  `;

const listSessions = async (
  query: string,
  limit: number,
  offset: number,
  sql: Sql = getSql()
) => {
  const trimmed = query.trim();
  const wildcard = `%${trimmed}%`;
  const queryFilter = trimmed
    ? sql`
        and (
          u.email ilike ${wildcard}
          or g.title ilike ${wildcard}
          or coalesce(pc.account_id, '') ilike ${wildcard}
          or p.key ilike ${wildcard}
        )
      `
    : sql``;

  return sql<AdminMovableSessionRow[]>`
    select
      ps.id as session_id,
      pc.id as source_credential_id,
      u.email as user_email,
      p.key as platform,
      pc.account_id as account_user,
      g.title as game_name,
      ps.started_at,
      ps.ended_at,
      greatest(
        0,
        floor(
          extract(epoch from (coalesce(ps.ended_at, now()) - ps.started_at))
        )
      )::integer as duration_seconds,
      (ps.ended_at is null) as live
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    join games g on g.id = ps.game_id
    where true
      ${queryFilter}
    order by ps.started_at desc, ps.id desc
    limit ${limit}
    offset ${offset}
  `;
};

const countSessions = async (query: string, sql: Sql = getSql()) => {
  const trimmed = query.trim();
  const wildcard = `%${trimmed}%`;
  const queryFilter = trimmed
    ? sql`
        and (
          u.email ilike ${wildcard}
          or g.title ilike ${wildcard}
          or coalesce(pc.account_id, '') ilike ${wildcard}
          or p.key ilike ${wildcard}
        )
      `
    : sql``;

  const rows = await sql<{ total: number }[]>`
    select count(*)::integer as total
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    join games g on g.id = ps.game_id
    where true
      ${queryFilter}
  `;
  return rows[0]?.total ?? 0;
};

const listPlatformStatuses = async (sql: Sql = getSql()) =>
  sql<AdminPlatformStatusRow[]>`
    select
      p.key as platform,
      count(pc.id)::integer as total_credentials,
      coalesce(sum(case when pc.active then 1 else 0 end), 0)::integer as active_credentials
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    group by p.key
    order by p.key asc
  `;

const loadPlatformByKey = async (platform: string, sql: Sql = getSql()) => {
  const rows = await sql<{ id: number }[]>`
    select id
    from platforms
    where key = ${platform}
    limit 1
  `;
  return rows[0] ?? null;
};

const updatePlatformCredentialsActive = async (
  platform: string,
  enabled: boolean,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    update platform_credentials pc
    set active = ${enabled}, updated_at = now()
    from platforms p
    where p.id = pc.platform_id
      and p.key = ${platform}
    returning pc.id
  `;
  return rows.length;
};

const listCredentials = async (sql: Sql = getSql()) =>
  sql<AdminCredentialRow[]>`
    select
      pc.id,
      u.email as user_email,
      p.key as platform,
      pc.api_key,
      pc.account_id,
      pc.active
    from platform_credentials pc
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    order by u.email asc, p.key asc, pc.id asc
  `;

const loadCredentialById = async (
  credentialId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<AdminCredentialRow[]>`
    select
      pc.id,
      u.email as user_email,
      p.key as platform,
      pc.api_key,
      pc.account_id,
      pc.active
    from platform_credentials pc
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    where pc.id = ${credentialId}
    limit 1
  `;
  return rows[0] ?? null;
};

const moveSessions = async (
  sessionIds: number[],
  targetCredentialId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    update play_sessions
    set platform_credential_id = ${targetCredentialId},
        updated_at = now()
    where id = any(${sql.array(sessionIds, 20)})
      and platform_credential_id <> ${targetCredentialId}
    returning id
  `;
  return rows.length;
};

const updateCredential = async (
  credentialId: number,
  changes: {
    apiKeyChanged: boolean;
    apiKey: string | null;
    userChanged: boolean;
    user: string | null;
  },
  sql: Sql = getSql()
) => {
  const rows = await sql<AdminCredentialRow[]>`
    update platform_credentials pc
    set
      api_key = case
        when ${changes.apiKeyChanged} then ${changes.apiKey}
        else pc.api_key
      end,
      account_id = case
        when ${changes.userChanged} then ${changes.user}
        else pc.account_id
      end,
      updated_at = now()
    from users u, platforms p
    where pc.id = ${credentialId}
      and u.id = pc.user_id
      and p.id = pc.platform_id
    returning
      pc.id,
      u.email as user_email,
      p.key as platform,
      pc.api_key,
      pc.account_id,
      pc.active
  `;
  return rows[0] ?? null;
};

const searchGames = async (
  query: string,
  gameId: number | null,
  limit: number,
  sql: Sql = getSql()
) => {
  const wildcard = `%${query}%`;
  const gameIdFilter = gameId ? sql`or g.id = ${gameId}` : sql``;

  return sql<AdminGameRow[]>`
    select
      g.id,
      g.title,
      g.title_locked,
      p.key as platform,
      g.capsule_url,
      g.capsule_url_locked
    from games g
    join platforms p on p.id = g.platform_id
    where g.title ilike ${wildcard}
      or coalesce(g.capsule_url, '') ilike ${wildcard}
      ${gameIdFilter}
    order by g.updated_at desc
    limit ${limit}
  `;
};

const updateGame = async (
  gameId: number,
  changes: {
    titleChanged: boolean;
    title: string;
    lockTitleChanged: boolean;
    lockTitle: boolean;
    imageUrlChanged: boolean;
    imageUrl: string | null;
    lockImageChanged: boolean;
    lockImage: boolean;
  },
  sql: Sql = getSql()
) => {
  const rows = await sql<AdminGameRow[]>`
    update games g
    set
      title = case
        when ${changes.titleChanged} then ${changes.title}
        else g.title
      end,
      title_locked = case
        when ${changes.lockTitleChanged} then ${changes.lockTitle}
        when ${changes.titleChanged} then true
        else g.title_locked
      end,
      capsule_url = case
        when ${changes.imageUrlChanged} then ${changes.imageUrl}
        else g.capsule_url
      end,
      capsule_url_locked = case
        when ${changes.lockImageChanged} then ${changes.lockImage}
        when ${changes.imageUrlChanged} then true
        else g.capsule_url_locked
      end,
      updated_at = now()
    from platforms p
    where g.id = ${gameId}
      and p.id = g.platform_id
    returning
      g.id,
      g.title,
      g.title_locked,
      p.key as platform,
      g.capsule_url,
      g.capsule_url_locked
  `;
  return rows[0] ?? null;
};

export const adminRepository = {
  listRecentSessions: () => listRecentSessions(),
  listSessions: (query: string, limit: number, offset: number) =>
    listSessions(query, limit, offset),
  countSessions: (query: string) => countSessions(query),
  listPlatformStatuses: () => listPlatformStatuses(),
  loadPlatformByKey: (platform: string) => loadPlatformByKey(platform),
  updatePlatformCredentialsActive: (platform: string, enabled: boolean) =>
    updatePlatformCredentialsActive(platform, enabled),
  listCredentials: () => listCredentials(),
  loadCredentialById: (credentialId: number) =>
    loadCredentialById(credentialId),
  moveSessions: (sessionIds: number[], targetCredentialId: number) =>
    moveSessions(sessionIds, targetCredentialId),
  updateCredential: (
    credentialId: number,
    changes: {
      apiKeyChanged: boolean;
      apiKey: string | null;
      userChanged: boolean;
      user: string | null;
    }
  ) => updateCredential(credentialId, changes),
  searchGames: (query: string, gameId: number | null, limit: number) =>
    searchGames(query, gameId, limit),
  updateGame: (
    gameId: number,
    changes: {
      titleChanged: boolean;
      title: string;
      lockTitleChanged: boolean;
      lockTitle: boolean;
      imageUrlChanged: boolean;
      imageUrl: string | null;
      lockImageChanged: boolean;
      lockImage: boolean;
    }
  ) => updateGame(gameId, changes),
};
