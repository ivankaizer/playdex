import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";
import type { NormalizedGame } from "../../common/domain/game";

type Sql = ReturnType<typeof postgres>;

type ProviderStatusRow = {
  id: number;
  platform: string;
  account_id: string | null;
  active: boolean;
  api_key: string | null;
};

type ManualInputRow = {
  id: number;
  raw_title: string;
  canonical_title: string;
  mapped_game_id: number | null;
  mapped_title: string | null;
  mapped_platform: string | null;
  mapped_capsule_url: string | null;
  mapped_canonical_key: string | null;
};

type ManualInputLookupRow = {
  id: number;
  raw_title: string;
  canonical_title: string;
  mapped_game_id: number | null;
};

type GameSearchRow = {
  id: number;
  title: string;
  platform: string;
  capsule_url: string | null;
};

const loadProviderByPlatform = async (
  userId: number,
  platform: string,
  sql: Sql = getSql()
) => {
  const rows = await sql<ProviderStatusRow[]>`
    select pc.id, p.key as platform, pc.account_id, pc.active, pc.api_key
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where pc.user_id = ${userId}
      and p.key = ${platform}
    limit 1
  `;
  return rows[0] ?? null;
};

const createManualCredential = async (userId: number, sql: Sql = getSql()) =>
  sql<ProviderStatusRow[]>`
    insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
    values (
      ${userId},
      (select id from platforms where key = 'manual'),
      'manual',
      'manual',
      true
    )
    returning
      id,
      (select key from platforms where key = 'manual') as platform,
      account_id,
      active,
      api_key
  `;

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

const ensureIgdbGame = async (
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
    insert into games (platform_id, title, external_id, canonical_key, capsule_url)
    values (
      coalesce(
        (select id from platforms where key = ${game.platform ?? "unknown"}),
        (select id from platforms where key = 'unknown')
      ),
      ${game.title},
      ${game.igdbId},
      ${canonicalKey},
      ${game.capsuleUrl}
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

const insertManualInput = async (
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

const updateManualInputTitle = async (
  inputId: number,
  rawTitle: string,
  sql: Sql = getSql()
) => sql`
  update manual_game_inputs
  set raw_title = ${rawTitle}, updated_at = now()
  where id = ${inputId}
`;

const loadManualInputByCanonical = async (
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

const updateManualInputMapping = async (
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

const listManualInputs = async (userId: number, sql: Sql = getSql()) =>
  sql<ManualInputRow[]>`
    select
      mgi.id,
      mgi.raw_title,
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
    order by mgi.updated_at desc
  `;

const loadPlatformKeyByIgdbId = async (
  igdbPlatformId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ key: string }[]>`
    select key
    from platforms
    where igdb_platform_id = ${igdbPlatformId}
    limit 1
  `;
  return rows[0]?.key ?? null;
};

const searchGamesByTitle = async (
  title: string,
  limit: number,
  sql: Sql = getSql()
) =>
  sql<GameSearchRow[]>`
    select g.id, g.title, p.key as platform, g.capsule_url
    from games g
    join platforms p on p.id = g.platform_id
    where g.title ilike ${`%${title}%`}
    order by g.updated_at desc
    limit ${limit}
  `;

export const manualRepository = {
  loadProviderByPlatform: (userId: number, platform: string) =>
    loadProviderByPlatform(userId, platform),

  createManualCredential: (userId: number) => createManualCredential(userId),

  endActiveSessionsForCredential: (credentialId: number, endedAt: Date) =>
    endActiveSessionsForCredential(credentialId, endedAt),

  ensureGameId: (game: NormalizedGame) => ensureGameId(game),

  ensureIgdbGame: (game: {
    igdbId: string;
    title: string;
    platform: string | null;
    capsuleUrl: string | null;
  }) => ensureIgdbGame(game),

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

  insertManualInput: (
    userId: number,
    rawTitle: string,
    canonicalTitle: string
  ) => insertManualInput(userId, rawTitle, canonicalTitle),

  updateManualInputTitle: (inputId: number, rawTitle: string) =>
    updateManualInputTitle(inputId, rawTitle),

  loadManualInputByCanonical: (userId: number, canonicalTitle: string) =>
    loadManualInputByCanonical(userId, canonicalTitle),

  updateManualInputMapping: (userId: number, inputId: number, gameId: number) =>
    updateManualInputMapping(userId, inputId, gameId),

  listManualInputs: (userId: number) => listManualInputs(userId),

  loadPlatformKeyByIgdbId: (platformId: number) =>
    loadPlatformKeyByIgdbId(platformId),

  searchGamesByTitle: (title: string, limit: number) =>
    searchGamesByTitle(title, limit),
};
