import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";
import type { NormalizedGame } from "../../common/domain/game";

type Sql = ReturnType<typeof postgres>;

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
  updated_at: Date;
};

type EndedSessionRow = {
  id: number;
  platform_credential_id: number;
  game_id: number;
  started_at: Date;
  ended_at: Date;
};

const loadPlatformCredentialsByApiKey = async (
  apiKey: string,
  platform: string,
  sql: Sql = getSql()
) =>
  sql<PlatformCredentialRow[]>`
    select pc.id, p.key as platform, pc.api_key, pc.account_id
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where pc.api_key = ${apiKey}
      and p.key = ${platform}
      and pc.active = true
  `;

const ensureGameId = async (game: NormalizedGame, sql: Sql = getSql()) => {
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
      title = excluded.title,
      platform_id = excluded.platform_id,
      external_id = excluded.external_id,
      capsule_url = coalesce(excluded.capsule_url, games.capsule_url),
      updated_at = now()
    returning id
  `;
  return rows[0].id;
};

const endActiveSessionsForCredential = async (
  platformCredentialId: number,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<EndedSessionRow[]>`
    update play_sessions
    set ended_at = ${endedAt}, updated_at = now()
    where platform_credential_id = ${platformCredentialId}
      and ended_at is null
    returning id, platform_credential_id, game_id, started_at, ended_at
  `;

const endActiveSessionsForCredentialExceptGame = async (
  platformCredentialId: number,
  gameId: number,
  endedAt: Date,
  sql: Sql = getSql()
) =>
  sql<EndedSessionRow[]>`
    update play_sessions
    set ended_at = ${endedAt}, updated_at = now()
    where platform_credential_id = ${platformCredentialId}
      and game_id <> ${gameId}
      and ended_at is null
    returning id, platform_credential_id, game_id, started_at, ended_at
  `;

const insertActivePlaySession = async (
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

const insertPlaySession = async (
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

const fetchActiveSessionForCredential = async (
  platformCredentialId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<ActiveSessionRow[]>`
    select id, game_id, started_at, updated_at
    from play_sessions
    where platform_credential_id = ${platformCredentialId}
      and ended_at is null
    order by started_at desc
    limit 1
  `;
  return rows[0] ?? null;
};

const touchActiveSession = async (
  sessionId: number,
  touchedAt: Date,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ id: number }[]>`
    update play_sessions
    set updated_at = ${touchedAt}
    where id = ${sessionId}
      and ended_at is null
    returning id
  `;
  return rows[0]?.id ?? null;
};

const endTimedOutActiveSessions = async (
  timeoutBefore: Date,
  endedAt: Date,
  timeoutMinutes: number,
  sql: Sql = getSql()
) =>
  sql<EndedSessionRow[]>`
    update play_sessions ps
    set ended_at = least(${endedAt}, ps.updated_at + (${timeoutMinutes} * interval '1 minute')),
        updated_at = now()
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where ps.platform_credential_id = pc.id
      and p.key = 'playnite'
      and ps.ended_at is null
      and ps.updated_at < ${timeoutBefore}
    returning ps.id, ps.platform_credential_id, ps.game_id, ps.started_at, ps.ended_at
  `;

const endTimedOutActiveSessionsForCredential = async (
  platformCredentialId: number,
  timeoutBefore: Date,
  endedAt: Date,
  timeoutMinutes: number,
  sql: Sql = getSql()
) =>
  sql<EndedSessionRow[]>`
    update play_sessions ps
    set ended_at = least(${endedAt}, ps.updated_at + (${timeoutMinutes} * interval '1 minute')),
        updated_at = now()
    where ps.platform_credential_id = ${platformCredentialId}
      and ps.ended_at is null
      and ps.updated_at < ${timeoutBefore}
    returning ps.id, ps.platform_credential_id, ps.game_id, ps.started_at, ps.ended_at
  `;

export const playniteRepository = {
  loadCredentialsByApiKey: (apiKey: string, platform: string) =>
    loadPlatformCredentialsByApiKey(apiKey, platform),

  ensureGameId: (game: NormalizedGame) => ensureGameId(game),

  endActiveSessionsForCredential: (credentialId: number, endedAt: Date) =>
    endActiveSessionsForCredential(credentialId, endedAt),

  endActiveSessionsForCredentialExceptGame: (
    credentialId: number,
    gameId: number,
    endedAt: Date
  ) => endActiveSessionsForCredentialExceptGame(credentialId, gameId, endedAt),

  endTimedOutActiveSessions: (
    timeoutBefore: Date,
    endedAt: Date,
    timeoutMinutes: number
  ) => endTimedOutActiveSessions(timeoutBefore, endedAt, timeoutMinutes),

  endTimedOutActiveSessionsForCredential: (
    credentialId: number,
    timeoutBefore: Date,
    endedAt: Date,
    timeoutMinutes: number
  ) =>
    endTimedOutActiveSessionsForCredential(
      credentialId,
      timeoutBefore,
      endedAt,
      timeoutMinutes
    ),

  fetchActiveSessionForCredential: (credentialId: number) =>
    fetchActiveSessionForCredential(credentialId),

  touchActiveSession: (sessionId: number, touchedAt: Date) =>
    touchActiveSession(sessionId, touchedAt),

  insertActivePlaySession: (
    credentialId: number,
    gameId: number,
    startedAt: Date
  ) => insertActivePlaySession(credentialId, gameId, startedAt),

  insertPlaySession: (
    credentialId: number,
    gameId: number,
    startedAt: Date,
    endedAt: Date
  ) => insertPlaySession(credentialId, gameId, startedAt, endedAt),
};
