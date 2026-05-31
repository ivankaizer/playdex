import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import { durationSecondsExpression } from "../../common/db/expressions";

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

const listRecentSessions = async (
  db: DbExecutor = getDb()
): Promise<AdminRecentSessionRow[]> => {
  // Row-number window function is kept as a sql fragment because Kysely
  // does not have a typed builder for PostgreSQL window clauses.
  const result = await sql<AdminRecentSessionRow>`
    with ranked as (
      select
        u.id as user_id,
        u.email,
        ps.id as session_id,
        g.title as game_name,
        ps.started_at,
        ${durationSecondsExpression()} as duration_seconds,
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
  `.execute(db);

  return result.rows;
};

const listSessions = async (
  query: string,
  limit: number,
  offset: number,
  db: DbExecutor = getDb()
): Promise<AdminMovableSessionRow[]> => {
  const trimmed = query.trim();
  const wildcard = `%${trimmed}%`;

  const result = await sql<AdminMovableSessionRow>`
    select
      ps.id as session_id,
      pc.id as source_credential_id,
      u.email as user_email,
      p.key as platform,
      pc.account_id as account_user,
      g.title as game_name,
      ps.started_at,
      ps.ended_at,
      ${durationSecondsExpression()} as duration_seconds,
      (ps.ended_at is null) as live
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    join games g on g.id = ps.game_id
    where true
      ${
        trimmed
          ? sql`
              and (
                u.email ilike ${wildcard}
                or g.title ilike ${wildcard}
                or coalesce(pc.account_id, '') ilike ${wildcard}
                or p.key ilike ${wildcard}
              )
            `
          : sql``
      }
    order by ps.started_at desc, ps.id desc
    limit ${limit}
    offset ${offset}
  `.execute(db);

  return result.rows;
};

const countSessions = async (query: string, db: DbExecutor = getDb()) => {
  const trimmed = query.trim();
  const wildcard = `%${trimmed}%`;

  const result = await sql<{ total: number }>`
    select count(*)::integer as total
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    join games g on g.id = ps.game_id
    where true
      ${
        trimmed
          ? sql`
              and (
                u.email ilike ${wildcard}
                or g.title ilike ${wildcard}
                or coalesce(pc.account_id, '') ilike ${wildcard}
                or p.key ilike ${wildcard}
              )
            `
          : sql``
      }
  `.execute(db);

  return result.rows[0]?.total ?? 0;
};

const listPlatformStatuses = async (db: DbExecutor = getDb()) =>
  db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select([
      "p.key as platform",
      sql<number>`count(pc.id)::integer`.as("total_credentials"),
      sql<number>`coalesce(sum(case when pc.active then 1 else 0 end), 0)::integer`.as(
        "active_credentials"
      ),
    ])
    .groupBy("p.key")
    .orderBy("p.key", "asc")
    .execute() as unknown as AdminPlatformStatusRow[];

const loadPlatformByKey = async (
  platform: string,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .selectFrom("platforms")
    .select("id")
    .where("key", "=", platform)
    .limit(1)
    .executeTakeFirst();
  return row ?? null;
};

const updatePlatformCredentialsActive = async (
  platform: string,
  enabled: boolean,
  db: DbExecutor = getDb()
) => {
  // UPDATE ... FROM cannot be clearly expressed with Kysely's typed builders.
  const result = await sql<{ id: string }>`
    update platform_credentials pc
    set active = ${enabled}, updated_at = now()
    from platforms p
    where p.id = pc.platform_id
      and p.key = ${platform}
    returning pc.id
  `.execute(db);

  return result.rows.length;
};

const listCredentials = async (
  db: DbExecutor = getDb()
): Promise<AdminCredentialRow[]> => {
  const rows = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("users as u", "u.id", "pc.user_id")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select([
      "pc.id",
      "u.email as user_email",
      "p.key as platform",
      "pc.api_key",
      "pc.account_id",
      "pc.active",
    ])
    .orderBy("u.email", "asc")
    .orderBy("p.key", "asc")
    .orderBy("pc.id", "asc")
    .execute();

  return rows.map((row) => ({
    ...row,
    id: Number(row.id),
  })) as unknown as AdminCredentialRow[];
};

const loadCredentialById = async (
  credentialId: number,
  db: DbExecutor = getDb()
): Promise<AdminCredentialRow | null> => {
  const row = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("users as u", "u.id", "pc.user_id")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select([
      "pc.id",
      "u.email as user_email",
      "p.key as platform",
      "pc.api_key",
      "pc.account_id",
      "pc.active",
    ])
    .where("pc.id", "=", String(credentialId))
    .limit(1)
    .executeTakeFirst();

  return row
    ? ({ ...row, id: Number(row.id) } as unknown as AdminCredentialRow)
    : null;
};

const moveSessions = async (
  sessionIds: number[],
  targetCredentialId: number,
  db: DbExecutor = getDb()
) => {
  const result = await sql<{ id: string }>`
    update play_sessions
    set platform_credential_id = ${targetCredentialId},
        updated_at = now()
    where id = any(ARRAY[${sql.join(
      sessionIds.map((id) => sql`${id}`)
    )}]::INTEGER[])
      and platform_credential_id <> ${targetCredentialId}
    returning id
  `.execute(db);

  return result.rows.length;
};

const updateCredential = async (
  credentialId: number,
  changes: {
    apiKeyChanged: boolean;
    apiKey: string | null;
    userChanged: boolean;
    user: string | null;
  },
  db: DbExecutor = getDb()
): Promise<AdminCredentialRow | null> => {
  // UPDATE ... FROM with conditional expressions cannot be clearly expressed
  // with Kysely's typed builders.
  const result = await sql<{
    id: string;
    user_email: string;
    platform: string;
    api_key: string | null;
    account_id: string | null;
    active: boolean;
  }>`
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
  `.execute(db);

  const row = result.rows[0];
  return row
    ? ({ ...row, id: Number(row.id) } as unknown as AdminCredentialRow)
    : null;
};

const searchGames = async (
  query: string,
  gameId: number | null,
  limit: number,
  db: DbExecutor = getDb()
): Promise<AdminGameRow[]> => {
  const wildcard = `%${query}%`;

  const result = await sql<AdminGameRow>`
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
      ${gameId ? sql`or g.id = ${gameId}` : sql``}
    order by g.updated_at desc
    limit ${limit}
  `.execute(db);

  return result.rows;
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
  db: DbExecutor = getDb()
): Promise<AdminGameRow | null> => {
  // UPDATE ... FROM with conditional CASE expressions cannot be clearly
  // expressed with Kysely's typed builders.
  const result = await sql<AdminGameRow>`
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
  `.execute(db);

  return result.rows[0] ?? null;
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
