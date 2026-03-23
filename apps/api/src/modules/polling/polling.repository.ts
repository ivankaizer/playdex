import type postgres from "postgres";
import type { JSONValue } from "postgres";

import { getSql } from "../../common/db/postgres";
import type { NormalizedGame } from "../../common/domain/game";

export type SqlClient = ReturnType<typeof postgres>;

type PlatformCredentialRow = {
  id: number;
  platform: string;
  api_key: string | null;
  account_id: string | null;
};

type ActiveSessionRow = {
  id: number;
  game_id: number;
  started_at: Date;
};

const loadPlatformCredentials = async (sql: SqlClient = getSql()) =>
  sql<PlatformCredentialRow[]>`
    select pc.id, p.key as platform, pc.api_key, pc.account_id
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where pc.active = true
  `;

const storeResolvedAccountId = async (
  credentialId: number,
  accountId: string,
  sql: SqlClient = getSql()
) => sql`
  update platform_credentials
  set account_id = ${accountId}, updated_at = now()
  where id = ${credentialId}
`;

const ensureGameId = async (
  game: NormalizedGame,
  sql: SqlClient = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    insert into games (platform_id, title, external_id, canonical_key, capsule_url)
    values (
      coalesce(
        (select id from platforms where key = ${game.platform}),
        (select id from platforms where key = 'unknown')
      ),
      ${game.title},
      ${game.externalId ?? null},
      ${game.canonicalKey},
      ${game.capsuleUrl ?? null}
    )
    on conflict (canonical_key)
    do update set
      title = case
        when games.title_locked then games.title
        else excluded.title
      end,
      platform_id = excluded.platform_id,
      external_id = excluded.external_id,
      capsule_url = case
        when games.capsule_url_locked then games.capsule_url
        else coalesce(excluded.capsule_url, games.capsule_url)
      end,
      updated_at = now()
    returning id
  `;
  return rows[0].id;
};

const isGameIgnoredForCredential = async (
  platformCredentialId: number,
  gameId: number,
  sql: SqlClient = getSql()
) => {
  const rows = await sql`
    select 1
    from ignored_games ig
    join platform_credentials pc on pc.user_id = ig.user_id
    where pc.id = ${platformCredentialId}
      and ig.game_id = ${gameId}
    limit 1
  `;
  return rows.length > 0;
};

const recordPollResult = async (
  platformCredentialId: number,
  gameId: number | null,
  polledAt: Date,
  rawPayload: unknown,
  sql: SqlClient = getSql()
) => {
  const payload =
    rawPayload === undefined ? null : sql.json(rawPayload as JSONValue);
  await sql`
    insert into poll_results (platform_credential_id, game_id, polled_at, raw_payload)
    values (${platformCredentialId}, ${gameId}, ${polledAt}, ${payload})
  `;
};

const fetchActivePlaySession = async (
  platformCredentialId: number,
  sql: SqlClient = getSql()
) => {
  const rows = await sql<ActiveSessionRow[]>`
    select id, game_id, started_at
    from play_sessions
    where platform_credential_id = ${platformCredentialId}
      and ended_at is null
    order by started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

const endPlaySession = async (
  sessionId: number,
  endedAt: Date,
  sql: SqlClient = getSql()
) => sql`
  update play_sessions
  set ended_at = ${endedAt}, updated_at = now()
  where id = ${sessionId}
`;

const startPlaySession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  sql: SqlClient = getSql()
) => sql`
  insert into play_sessions (platform_credential_id, game_id, started_at)
  values (${platformCredentialId}, ${gameId}, ${startedAt})
`;

export const pollingRepository = {
  fetchActivePlaySession: (platformCredentialId: number, sql?: SqlClient) =>
    fetchActivePlaySession(platformCredentialId, sql),

  endPlaySession: (sessionId: number, endedAt: Date, sql?: SqlClient) =>
    endPlaySession(sessionId, endedAt, sql),

  startPlaySession: (
    platformCredentialId: number,
    gameId: number,
    startedAt: Date,
    sql?: SqlClient
  ) => startPlaySession(platformCredentialId, gameId, startedAt, sql),

  ensureGameId: (game: NormalizedGame, sql?: SqlClient) =>
    ensureGameId(game, sql),

  isGameIgnoredForCredential: (
    platformCredentialId: number,
    gameId: number,
    sql?: SqlClient
  ) => isGameIgnoredForCredential(platformCredentialId, gameId, sql),

  recordPollResult: (
    platformCredentialId: number,
    gameId: number | null,
    polledAt: Date,
    rawPayload: unknown,
    sql?: SqlClient
  ) =>
    recordPollResult(platformCredentialId, gameId, polledAt, rawPayload, sql),

  loadPlatformCredentials: () => loadPlatformCredentials(),

  storeResolvedAccountId: (credentialId: number, accountId: string) =>
    storeResolvedAccountId(credentialId, accountId),
};
