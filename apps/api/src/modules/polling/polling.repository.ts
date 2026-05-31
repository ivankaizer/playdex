import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";
import { upsertGame } from "../../common/db/queries/games";
import type { JsonValue } from "../../common/db/schema";
import type { NormalizedGame } from "../../common/domain/game";

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

const loadPlatformCredentials = async (
  db: DbExecutor = getDb()
): Promise<PlatformCredentialRow[]> => {
  const rows = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select(["pc.id", "p.key as platform", "pc.api_key", "pc.account_id"])
    .where("pc.active", "=", true)
    .execute();

  return rows.map((row) => ({
    ...row,
    id: toDomainId(row.id, "platform_credentials.id"),
  }));
};

const storeResolvedAccountId = (
  credentialId: number,
  accountId: string,
  db: DbExecutor = getDb()
) =>
  db
    .updateTable("platform_credentials")
    .set({ account_id: accountId, updated_at: sql`now()` })
    .where("id", "=", String(credentialId))
    .execute();

const isGameIgnoredForCredential = async (
  platformCredentialId: number,
  gameId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .selectFrom("ignored_games as ig")
    .innerJoin("platform_credentials as pc", "pc.user_id", "ig.user_id")
    .select("ig.id")
    .where("pc.id", "=", String(platformCredentialId))
    .where("ig.game_id", "=", String(gameId))
    .executeTakeFirst();

  return Boolean(row);
};

const recordPollResult = (
  platformCredentialId: number,
  gameId: number | null,
  polledAt: Date,
  rawPayload: unknown,
  db: DbExecutor = getDb()
) =>
  db
    .insertInto("poll_results")
    .values({
      platform_credential_id: platformCredentialId,
      game_id: gameId,
      polled_at: polledAt,
      raw_payload: rawPayload === undefined ? null : (rawPayload as JsonValue),
    })
    .execute();

const fetchActivePlaySession = async (
  platformCredentialId: number,
  db: DbExecutor = getDb()
): Promise<ActiveSessionRow | null> => {
  const row = await db
    .selectFrom("play_sessions")
    .select(["id", "game_id", "started_at"])
    .where("platform_credential_id", "=", String(platformCredentialId))
    .where("ended_at", "is", null)
    .orderBy("started_at", "desc")
    .executeTakeFirst();

  return row
    ? {
        ...row,
        id: toDomainId(row.id, "play_sessions.id"),
        game_id: toDomainId(row.game_id, "play_sessions.game_id"),
      }
    : null;
};

const endPlaySession = (
  sessionId: number,
  endedAt: Date,
  db: DbExecutor = getDb()
) =>
  db
    .updateTable("play_sessions")
    .set({ ended_at: endedAt, updated_at: sql`now()` })
    .where("id", "=", String(sessionId))
    .execute();

const startPlaySession = (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  db: DbExecutor = getDb()
) =>
  db
    .insertInto("play_sessions")
    .values({
      platform_credential_id: platformCredentialId,
      game_id: gameId,
      started_at: startedAt,
    })
    .execute();

export const pollingRepository = {
  fetchActivePlaySession: (platformCredentialId: number, db?: DbExecutor) =>
    fetchActivePlaySession(platformCredentialId, db),

  endPlaySession: (sessionId: number, endedAt: Date, db?: DbExecutor) =>
    endPlaySession(sessionId, endedAt, db),

  startPlaySession: (
    platformCredentialId: number,
    gameId: number,
    startedAt: Date,
    db?: DbExecutor
  ) => startPlaySession(platformCredentialId, gameId, startedAt, db),

  ensureGameId: (game: NormalizedGame, db?: DbExecutor) =>
    upsertGame(db ?? getDb(), game),

  isGameIgnoredForCredential: (
    platformCredentialId: number,
    gameId: number,
    db?: DbExecutor
  ) => isGameIgnoredForCredential(platformCredentialId, gameId, db),

  recordPollResult: (
    platformCredentialId: number,
    gameId: number | null,
    polledAt: Date,
    rawPayload: unknown,
    db?: DbExecutor
  ) => recordPollResult(platformCredentialId, gameId, polledAt, rawPayload, db),

  loadPlatformCredentials: (db?: DbExecutor) => loadPlatformCredentials(db),

  storeResolvedAccountId: (
    credentialId: number,
    accountId: string,
    db?: DbExecutor
  ) => storeResolvedAccountId(credentialId, accountId, db),
};
