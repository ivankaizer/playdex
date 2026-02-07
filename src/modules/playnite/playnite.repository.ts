import type postgres from "postgres";

import { getSql } from "../../shared/db/postgres";
import type { NormalizedGame } from "../../common/domain/game";

type Sql = ReturnType<typeof postgres>;

type PlatformCredentialRow = {
  id: number;
  platform: string;
  api_key: string | null;
  account_id: string | null;
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
  sql<{ id: number; game_id: number; started_at: Date }[]>`
    update play_sessions
    set ended_at = ${endedAt}, updated_at = now()
    where platform_credential_id = ${platformCredentialId}
      and ended_at is null
    returning id, game_id, started_at
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

export const playniteRepository = {
  loadCredentialsByApiKey: (apiKey: string, platform: string) =>
    loadPlatformCredentialsByApiKey(apiKey, platform),

  ensureGameId: (game: NormalizedGame) => ensureGameId(game),

  endActiveSessionsForCredential: (credentialId: number, endedAt: Date) =>
    endActiveSessionsForCredential(credentialId, endedAt),

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
