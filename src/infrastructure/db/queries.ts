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
};

export type ActiveSessionRow = {
  id: number;
  game_id: number;
};

export type UserAuthRow = {
  id: number;
  email: string;
  password: string | null;
};

export const loadPlatformCredentials = async (sql: Sql = getSql()) =>
  sql<PlatformCredentialRow[]>`
    select id, platform, api_key, account_id
    from platform_credentials
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

export const listAccountSessions = async (
  accountId: number,
  sql: Sql = getSql()
) =>
  sql<AccountSessionRow[]>`
    select
      ps.id,
      ps.started_at,
      ps.ended_at,
      pc.platform,
      g.title
    from play_sessions ps
    join platform_credentials pc on pc.id = ps.platform_credential_id
    join games g on g.id = ps.game_id
    where ps.platform_credential_id = ${accountId}
    order by
      case when ps.ended_at is null then 0 else 1 end,
      ps.started_at desc
    limit 10
  `;

export const loadUserByEmail = async (
  email: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<UserAuthRow[]>`
    select id, email, password
    from users
    where email = ${email}
    limit 1
  `;
  return rows[0] ?? null;
};

export const ensureGameId = async (
  game: NormalizedGame,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    insert into games (platform, title, external_id, canonical_key)
    values (${game.platform}, ${game.title}, ${game.externalId ?? null}, ${game.canonicalKey})
    on conflict (canonical_key)
    do update set
      title = excluded.title,
      external_id = excluded.external_id,
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
