import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";
import { upsertGame } from "../../common/db/queries/games";
import type { NormalizedGame } from "../../common/domain/game";

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

const toOptionalDomainId = (value: string | null, field: string) =>
  value === null ? null : toDomainId(value, field);

const loadProviderByPlatform = async (
  userId: number,
  platform: string,
  db: DbExecutor = getDb()
): Promise<ProviderStatusRow | null> => {
  const row = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select([
      "pc.id",
      "p.key as platform",
      "pc.account_id",
      "pc.active",
      "pc.api_key",
    ])
    .where("pc.user_id", "=", String(userId))
    .where("p.key", "=", platform)
    .executeTakeFirst();

  return row
    ? { ...row, id: toDomainId(row.id, "platform_credentials.id") }
    : null;
};

const createManualCredential = async (
  userId: number,
  db: DbExecutor = getDb()
): Promise<ProviderStatusRow[]> => {
  const row = await db
    .insertInto("platform_credentials")
    .values((expression) => ({
      user_id: userId,
      platform_id: expression
        .selectFrom("platforms")
        .select("id")
        .where("key", "=", "manual"),
      api_key: "manual",
      account_id: "manual",
      active: true,
    }))
    .returning(["id", "account_id", "active", "api_key"])
    .executeTakeFirstOrThrow();

  return [
    {
      ...row,
      id: toDomainId(row.id, "platform_credentials.id"),
      platform: "manual",
    },
  ];
};

const endActiveSessionsForCredential = async (
  platformCredentialId: number,
  endedAt: Date,
  db: DbExecutor = getDb()
) => {
  const rows = await db
    .updateTable("play_sessions")
    .set({ ended_at: endedAt, updated_at: sql`now()` })
    .where("platform_credential_id", "=", String(platformCredentialId))
    .where("ended_at", "is", null)
    .returning(["id", "game_id", "started_at"])
    .execute();

  return rows.map((row) => ({
    ...row,
    id: toDomainId(row.id, "play_sessions.id"),
    game_id: toDomainId(row.game_id, "play_sessions.game_id"),
  }));
};

const ensureIgdbGame = (
  game: {
    igdbId: string;
    title: string;
    platform: string | null;
    capsuleUrl: string | null;
  },
  db: DbExecutor = getDb()
) =>
  upsertGame(db, {
    platform: game.platform ?? "unknown",
    title: game.title,
    externalId: game.igdbId,
    canonicalTitle: game.title,
    canonicalKey: `igdb:${game.igdbId}`,
    capsuleUrl: game.capsuleUrl ?? undefined,
  });

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

const insertPlaySession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt: Date,
  endedAt: Date,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .insertInto("play_sessions")
    .values({
      platform_credential_id: platformCredentialId,
      game_id: gameId,
      started_at: startedAt,
      ended_at: endedAt,
    })
    .returning("id")
    .executeTakeFirst();

  return row ? toDomainId(row.id, "play_sessions.id") : null;
};

const insertManualInput = async (
  userId: number,
  rawTitle: string,
  canonicalTitle: string,
  db: DbExecutor = getDb()
): Promise<ManualInputLookupRow | null> => {
  const row = await db
    .insertInto("manual_game_inputs")
    .values({
      user_id: userId,
      raw_title: rawTitle,
      canonical_title: canonicalTitle,
    })
    .returning(["id", "raw_title", "canonical_title", "mapped_game_id"])
    .executeTakeFirst();

  return row
    ? {
        ...row,
        id: toDomainId(row.id, "manual_game_inputs.id"),
        mapped_game_id: toOptionalDomainId(
          row.mapped_game_id,
          "manual_game_inputs.mapped_game_id"
        ),
      }
    : null;
};

const updateManualInputTitle = (
  inputId: number,
  rawTitle: string,
  db: DbExecutor = getDb()
) =>
  db
    .updateTable("manual_game_inputs")
    .set({ raw_title: rawTitle, updated_at: sql`now()` })
    .where("id", "=", String(inputId))
    .execute();

const loadManualInputByCanonical = async (
  userId: number,
  canonicalTitle: string,
  db: DbExecutor = getDb()
): Promise<ManualInputLookupRow | null> => {
  const row = await db
    .selectFrom("manual_game_inputs")
    .select(["id", "raw_title", "canonical_title", "mapped_game_id"])
    .where("user_id", "=", String(userId))
    .where("canonical_title", "=", canonicalTitle)
    .executeTakeFirst();

  return row
    ? {
        ...row,
        id: toDomainId(row.id, "manual_game_inputs.id"),
        mapped_game_id: toOptionalDomainId(
          row.mapped_game_id,
          "manual_game_inputs.mapped_game_id"
        ),
      }
    : null;
};

const updateManualInputMapping = async (
  userId: number,
  inputId: number,
  gameId: number,
  db: DbExecutor = getDb()
): Promise<ManualInputLookupRow | null> => {
  const row = await db
    .updateTable("manual_game_inputs")
    .set({ mapped_game_id: gameId, updated_at: sql`now()` })
    .where("id", "=", String(inputId))
    .where("user_id", "=", String(userId))
    .returning(["id", "raw_title", "canonical_title", "mapped_game_id"])
    .executeTakeFirst();

  return row
    ? {
        ...row,
        id: toDomainId(row.id, "manual_game_inputs.id"),
        mapped_game_id: toOptionalDomainId(
          row.mapped_game_id,
          "manual_game_inputs.mapped_game_id"
        ),
      }
    : null;
};

const listManualInputs = async (
  userId: number,
  db: DbExecutor = getDb()
): Promise<ManualInputRow[]> => {
  const rows = await db
    .selectFrom("manual_game_inputs as mgi")
    .leftJoin("games as g", "g.id", "mgi.mapped_game_id")
    .leftJoin("platforms as gp", "gp.id", "g.platform_id")
    .select([
      "mgi.id",
      "mgi.raw_title",
      "mgi.canonical_title",
      "mgi.mapped_game_id",
      "g.title as mapped_title",
      "gp.key as mapped_platform",
      "g.capsule_url as mapped_capsule_url",
      "g.canonical_key as mapped_canonical_key",
    ])
    .where("mgi.user_id", "=", String(userId))
    .orderBy("mgi.updated_at", "desc")
    .execute();

  return rows.map((row) => ({
    ...row,
    id: toDomainId(row.id, "manual_game_inputs.id"),
    mapped_game_id: toOptionalDomainId(
      row.mapped_game_id,
      "manual_game_inputs.mapped_game_id"
    ),
  }));
};

const loadPlatformKeyByIgdbId = async (
  igdbPlatformId: number,
  db: DbExecutor = getDb()
) =>
  (
    await db
      .selectFrom("platforms")
      .select("key")
      .where("igdb_platform_id", "=", igdbPlatformId)
      .executeTakeFirst()
  )?.key ?? null;

const searchGamesByTitle = async (
  title: string,
  limit: number,
  db: DbExecutor = getDb()
): Promise<GameSearchRow[]> => {
  const rows = await db
    .selectFrom("games as g")
    .innerJoin("platforms as p", "p.id", "g.platform_id")
    .select(["g.id", "g.title", "p.key as platform", "g.capsule_url"])
    .where("g.title", "ilike", `%${title}%`)
    .orderBy("g.updated_at", "desc")
    .limit(limit)
    .execute();

  return rows.map((row) => ({
    ...row,
    id: toDomainId(row.id, "games.id"),
  }));
};

export const manualRepository = {
  loadProviderByPlatform: (userId: number, platform: string, db?: DbExecutor) =>
    loadProviderByPlatform(userId, platform, db),

  createManualCredential: (userId: number, db?: DbExecutor) =>
    createManualCredential(userId, db),

  endActiveSessionsForCredential: (
    credentialId: number,
    endedAt: Date,
    db?: DbExecutor
  ) => endActiveSessionsForCredential(credentialId, endedAt, db),

  ensureGameId: (game: NormalizedGame, db?: DbExecutor) =>
    upsertGame(db ?? getDb(), game),

  ensureIgdbGame: (
    game: {
      igdbId: string;
      title: string;
      platform: string | null;
      capsuleUrl: string | null;
    },
    db?: DbExecutor
  ) => ensureIgdbGame(game, db),

  insertActivePlaySession: (
    credentialId: number,
    gameId: number,
    startedAt: Date,
    db?: DbExecutor
  ) => insertActivePlaySession(credentialId, gameId, startedAt, db),

  insertPlaySession: (
    credentialId: number,
    gameId: number,
    startedAt: Date,
    endedAt: Date,
    db?: DbExecutor
  ) => insertPlaySession(credentialId, gameId, startedAt, endedAt, db),

  insertManualInput: (
    userId: number,
    rawTitle: string,
    canonicalTitle: string,
    db?: DbExecutor
  ) => insertManualInput(userId, rawTitle, canonicalTitle, db),

  updateManualInputTitle: (
    inputId: number,
    rawTitle: string,
    db?: DbExecutor
  ) => updateManualInputTitle(inputId, rawTitle, db),

  loadManualInputByCanonical: (
    userId: number,
    canonicalTitle: string,
    db?: DbExecutor
  ) => loadManualInputByCanonical(userId, canonicalTitle, db),

  updateManualInputMapping: (
    userId: number,
    inputId: number,
    gameId: number,
    db?: DbExecutor
  ) => updateManualInputMapping(userId, inputId, gameId, db),

  listManualInputs: (userId: number, db?: DbExecutor) =>
    listManualInputs(userId, db),

  loadPlatformKeyByIgdbId: (platformId: number, db?: DbExecutor) =>
    loadPlatformKeyByIgdbId(platformId, db),

  searchGamesByTitle: (title: string, limit: number, db?: DbExecutor) =>
    searchGamesByTitle(title, limit, db),
};
