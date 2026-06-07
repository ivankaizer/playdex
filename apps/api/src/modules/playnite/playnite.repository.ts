import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";
import { upsertGame } from "../../common/db/queries/games";
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
  updated_at: Date;
};

type EndedSessionRow = {
  id: number;
  platform_credential_id: number;
  game_id: number;
  started_at: Date;
  ended_at: Date;
};

type DbEndedSessionRow = Omit<
  EndedSessionRow,
  "id" | "platform_credential_id" | "game_id"
> & {
  id: string;
  platform_credential_id: string;
  game_id: string;
};

const toEndedSessionRow = (row: DbEndedSessionRow): EndedSessionRow => ({
  ...row,
  id: toDomainId(row.id, "play_sessions.id"),
  platform_credential_id: toDomainId(
    row.platform_credential_id,
    "play_sessions.platform_credential_id"
  ),
  game_id: toDomainId(row.game_id, "play_sessions.game_id"),
});

const loadPlatformCredentialsByApiKey = async (
  apiKey: string,
  platform: string,
  db: DbExecutor = getDb()
): Promise<PlatformCredentialRow[]> => {
  const rows = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select(["pc.id", "p.key as platform", "pc.api_key", "pc.account_id"])
    .where("pc.api_key", "=", apiKey)
    .where("p.key", "=", platform)
    .where("pc.active", "=", true)
    .execute();

  return rows.map((row) => ({
    ...row,
    id: toDomainId(row.id, "platform_credentials.id"),
  }));
};

const endActiveSessionsForCredentialAndGame = async (
  platformCredentialId: number,
  gameId: number,
  endedAt: Date,
  db: DbExecutor = getDb()
) =>
  (
    await db
      .updateTable("play_sessions")
      .set({ ended_at: endedAt, updated_at: sql`now()` })
      .where("platform_credential_id", "=", String(platformCredentialId))
      .where("game_id", "=", String(gameId))
      .where("ended_at", "is", null)
      .returning([
        "id",
        "platform_credential_id",
        "game_id",
        "started_at",
        "ended_at",
      ])
      .execute()
  ).map((row) => toEndedSessionRow(row as DbEndedSessionRow));

const insertActivePlaySession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .insertInto("play_sessions")
    .values({
      platform_credential_id: platformCredentialId,
      game_id: gameId,
      started_at: startedAt,
    })
    .returning("id")
    .executeTakeFirst();

  return row ? toDomainId(row.id, "play_sessions.id") : null;
};

const fetchActiveSessionForCredentialAndGame = async (
  platformCredentialId: number,
  gameId: number,
  db: DbExecutor = getDb()
): Promise<ActiveSessionRow | null> => {
  const row = await db
    .selectFrom("play_sessions")
    .select(["id", "game_id", "started_at", "updated_at"])
    .where("platform_credential_id", "=", String(platformCredentialId))
    .where("game_id", "=", String(gameId))
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

const touchActiveSession = async (
  sessionId: number,
  touchedAt: Date,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .updateTable("play_sessions")
    .set({ updated_at: touchedAt })
    .where("id", "=", String(sessionId))
    .where("ended_at", "is", null)
    .returning("id")
    .executeTakeFirst();

  return row ? toDomainId(row.id, "play_sessions.id") : null;
};

const endTimedOutActiveSessions = async (
  timeoutBefore: Date,
  endedAt: Date,
  timeoutMinutes: number,
  db: DbExecutor = getDb()
) => {
  // Kysely's builder is less clear for PostgreSQL UPDATE FROM plus interval math.
  const { rows } = await sql<DbEndedSessionRow>`
    update play_sessions ps
    set ended_at = least(
          ${endedAt},
          ps.updated_at + (${timeoutMinutes} * interval '1 minute')
        ),
        updated_at = now()
    from platform_credentials pc
    join platforms p on p.id = pc.platform_id
    where ps.platform_credential_id = pc.id
      and p.key = 'playnite'
      and ps.ended_at is null
      and ps.updated_at < ${timeoutBefore}
    returning ps.id, ps.platform_credential_id, ps.game_id, ps.started_at, ps.ended_at
  `.execute(db);

  return rows.map(toEndedSessionRow);
};

const endTimedOutActiveSessionsForCredential = async (
  platformCredentialId: number,
  timeoutBefore: Date,
  endedAt: Date,
  timeoutMinutes: number,
  db: DbExecutor = getDb()
) => {
  const rows = await db
    .updateTable("play_sessions")
    .set({
      ended_at: sql`least(
        ${endedAt},
        updated_at + (${timeoutMinutes} * interval '1 minute')
      )`,
      updated_at: sql`now()`,
    })
    .where("platform_credential_id", "=", String(platformCredentialId))
    .where("ended_at", "is", null)
    .where("updated_at", "<", timeoutBefore)
    .returning([
      "id",
      "platform_credential_id",
      "game_id",
      "started_at",
      "ended_at",
    ])
    .execute();

  return rows.map((row) => toEndedSessionRow(row as DbEndedSessionRow));
};

export const playniteRepository = {
  loadCredentialsByApiKey: (
    apiKey: string,
    platform: string,
    db?: DbExecutor
  ) => loadPlatformCredentialsByApiKey(apiKey, platform, db),

  ensureGameId: (game: NormalizedGame, db?: DbExecutor) =>
    upsertGame(db ?? getDb(), game),

  endActiveSessionsForCredentialAndGame: (
    credentialId: number,
    gameId: number,
    endedAt: Date,
    db?: DbExecutor
  ) =>
    endActiveSessionsForCredentialAndGame(credentialId, gameId, endedAt, db),

  endTimedOutActiveSessions: (
    timeoutBefore: Date,
    endedAt: Date,
    timeoutMinutes: number,
    db?: DbExecutor
  ) => endTimedOutActiveSessions(timeoutBefore, endedAt, timeoutMinutes, db),

  endTimedOutActiveSessionsForCredential: (
    credentialId: number,
    timeoutBefore: Date,
    endedAt: Date,
    timeoutMinutes: number,
    db?: DbExecutor
  ) =>
    endTimedOutActiveSessionsForCredential(
      credentialId,
      timeoutBefore,
      endedAt,
      timeoutMinutes,
      db
    ),

  fetchActiveSessionForCredentialAndGame: (
    credentialId: number,
    gameId: number,
    db?: DbExecutor
  ) => fetchActiveSessionForCredentialAndGame(credentialId, gameId, db),

  touchActiveSession: (sessionId: number, touchedAt: Date, db?: DbExecutor) =>
    touchActiveSession(sessionId, touchedAt, db),

  insertActivePlaySession: (
    credentialId: number,
    gameId: number,
    startedAt: Date,
    db?: DbExecutor
  ) => insertActivePlaySession(credentialId, gameId, startedAt, db),

};
