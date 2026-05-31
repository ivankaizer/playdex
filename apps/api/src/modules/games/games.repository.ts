import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";

type GameRow = {
  id: number;
  title: string;
  platform: string;
  capsule_url: string | null;
};

type DbGameRow = Omit<GameRow, "id"> & {
  id: string;
};

type GameRatingRow = {
  game_id: number;
  rating: number;
};

type DbGameRatingRow = Omit<GameRatingRow, "game_id"> & {
  game_id: string;
};

type GameSearchRow = GameRow;

type GameConnectionEdgeRow = {
  game_id: number;
  connected_game_id: number;
};

type DbGameConnectionEdgeRow = {
  game_id: string;
  connected_game_id: string;
};

type GameConnectionRow = {
  game_id: number;
  game_title: string;
  game_platform: string;
  game_capsule_url: string | null;
  connected_game_id: number;
  connected_title: string;
  connected_platform: string;
  connected_capsule_url: string | null;
};

type DbGameConnectionRow = Omit<
  GameConnectionRow,
  "game_id" | "connected_game_id"
> & {
  game_id: string;
  connected_game_id: string;
};

const toDbId = (id: number) => String(id);

const mapGameRow = (row: DbGameRow): GameRow => ({
  ...row,
  id: toDomainId(row.id, "games.id"),
});

const mapGameRatingRow = (row: DbGameRatingRow): GameRatingRow => ({
  ...row,
  game_id: toDomainId(row.game_id, "game_ratings.game_id"),
});

const mapGameConnectionEdgeRow = (
  row: DbGameConnectionEdgeRow
): GameConnectionEdgeRow => ({
  game_id: toDomainId(row.game_id, "game_connections.game_id"),
  connected_game_id: toDomainId(
    row.connected_game_id,
    "game_connections.connected_game_id"
  ),
});

const mapGameConnectionRow = (row: DbGameConnectionRow): GameConnectionRow => ({
  ...row,
  game_id: toDomainId(row.game_id, "game_connections.game_id"),
  connected_game_id: toDomainId(
    row.connected_game_id,
    "game_connections.connected_game_id"
  ),
});

const ignoreGameForUser = async (
  userId: number,
  gameId: number,
  db: DbExecutor = getDb()
) =>
  db
    .insertInto("ignored_games")
    .values({ user_id: userId, game_id: gameId })
    .onConflict((oc) =>
      oc
        .columns(["user_id", "game_id"])
        .doUpdateSet({ updated_at: sql<Date>`now()` })
    )
    .execute();

const unignoreGameForUser = async (
  userId: number,
  gameId: number,
  db: DbExecutor = getDb()
) =>
  db
    .deleteFrom("ignored_games")
    .where("user_id", "=", toDbId(userId))
    .where("game_id", "=", toDbId(gameId))
    .execute();

const isGameIgnoredForUser = async (
  userId: number,
  gameId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .selectFrom("ignored_games")
    .select("id")
    .where("user_id", "=", toDbId(userId))
    .where("game_id", "=", toDbId(gameId))
    .limit(1)
    .executeTakeFirst();
  return Boolean(row);
};

const loadGameRatingForUser = async (
  userId: number,
  gameId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .selectFrom("game_ratings")
    .select(["game_id", "rating"])
    .where("user_id", "=", toDbId(userId))
    .where("game_id", "=", toDbId(gameId))
    .limit(1)
    .executeTakeFirst();
  return row ? mapGameRatingRow(row) : null;
};

const upsertGameRatingForUser = async (
  userId: number,
  gameId: number,
  rating: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .insertInto("game_ratings")
    .values({ user_id: userId, game_id: gameId, rating })
    .onConflict((oc) =>
      oc.columns(["user_id", "game_id"]).doUpdateSet((eb) => ({
        rating: eb.ref("excluded.rating"),
        updated_at: sql<Date>`now()`,
      }))
    )
    .returning(["game_id", "rating"])
    .executeTakeFirst();
  return row ? mapGameRatingRow(row) : null;
};

const deleteGameRatingForUser = async (
  userId: number,
  gameId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .deleteFrom("game_ratings")
    .where("user_id", "=", toDbId(userId))
    .where("game_id", "=", toDbId(gameId))
    .returning("game_id")
    .executeTakeFirst();
  return Boolean(row);
};

const loadGameById = async (gameId: number, db: DbExecutor = getDb()) => {
  const row = await db
    .selectFrom("games as g")
    .innerJoin("platforms as p", "p.id", "g.platform_id")
    .select(["g.id", "g.title", "p.key as platform", "g.capsule_url"])
    .where("g.id", "=", toDbId(gameId))
    .limit(1)
    .executeTakeFirst();
  return row ? mapGameRow(row) : null;
};

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
  return rows.map(mapGameRow);
};

const listGameConnections = async (
  userId: number,
  db: DbExecutor = getDb()
) => {
  const rows = await db
    .selectFrom("game_connections as gc")
    .innerJoin("games as g", "g.id", "gc.game_id")
    .innerJoin("platforms as gp", "gp.id", "g.platform_id")
    .innerJoin("games as cg", "cg.id", "gc.connected_game_id")
    .innerJoin("platforms as cgp", "cgp.id", "cg.platform_id")
    .select([
      "gc.game_id",
      "g.title as game_title",
      "gp.key as game_platform",
      "g.capsule_url as game_capsule_url",
      "gc.connected_game_id",
      "cg.title as connected_title",
      "cgp.key as connected_platform",
      "cg.capsule_url as connected_capsule_url",
    ])
    .where("gc.user_id", "=", toDbId(userId))
    .orderBy("g.title")
    .orderBy("cg.title")
    .execute();
  return rows.map(mapGameConnectionRow);
};

const listGameConnectionEdges = async (
  userId: number,
  db: DbExecutor = getDb()
) => {
  const rows = await db
    .selectFrom("game_connections")
    .select(["game_id", "connected_game_id"])
    .where("user_id", "=", toDbId(userId))
    .execute();
  return rows.map(mapGameConnectionEdgeRow);
};

const insertGameConnection = async (
  userId: number,
  gameId: number,
  connectedGameId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .insertInto("game_connections")
    .values({
      user_id: userId,
      game_id: gameId,
      connected_game_id: connectedGameId,
    })
    .onConflict((oc) =>
      oc
        .columns(["user_id", "game_id", "connected_game_id"])
        .doUpdateSet({ updated_at: sql<Date>`now()` })
    )
    .returning(["game_id", "connected_game_id"])
    .executeTakeFirst();
  return row ? mapGameConnectionEdgeRow(row) : null;
};

const deleteGameConnection = async (
  userId: number,
  gameId: number,
  connectedGameId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .deleteFrom("game_connections")
    .where("user_id", "=", toDbId(userId))
    .where("game_id", "=", toDbId(gameId))
    .where("connected_game_id", "=", toDbId(connectedGameId))
    .returning(["game_id", "connected_game_id"])
    .executeTakeFirst();
  return row ? mapGameConnectionEdgeRow(row) : null;
};

export const gamesRepository = {
  getIgnoreStatus: (userId: number, gameId: number) =>
    isGameIgnoredForUser(userId, gameId),

  ignoreGame: (userId: number, gameId: number) =>
    ignoreGameForUser(userId, gameId),

  unignoreGame: (userId: number, gameId: number) =>
    unignoreGameForUser(userId, gameId),

  getRating: (userId: number, gameId: number) =>
    loadGameRatingForUser(userId, gameId),

  upsertRating: (userId: number, gameId: number, rating: number) =>
    upsertGameRatingForUser(userId, gameId, rating),

  deleteRating: (userId: number, gameId: number) =>
    deleteGameRatingForUser(userId, gameId),

  getGameById: (gameId: number) => loadGameById(gameId),

  searchGames: (query: string, limit: number) =>
    searchGamesByTitle(query, limit),

  listGameConnections: (userId: number) => listGameConnections(userId),

  listConnectionEdges: (userId: number) => listGameConnectionEdges(userId),

  createGameConnection: (
    userId: number,
    gameId: number,
    connectedGameId: number
  ) => insertGameConnection(userId, gameId, connectedGameId),

  deleteGameConnection: (
    userId: number,
    gameId: number,
    connectedGameId: number
  ) => deleteGameConnection(userId, gameId, connectedGameId),
};
