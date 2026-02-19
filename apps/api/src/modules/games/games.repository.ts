import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

type Sql = ReturnType<typeof postgres>;

type GameRow = {
  id: number;
  title: string;
  platform: string;
  capsule_url: string | null;
};

type GameRatingRow = {
  game_id: number;
  rating: number;
};

type GameSearchRow = {
  id: number;
  title: string;
  platform: string;
  capsule_url: string | null;
};

type GameConnectionEdgeRow = {
  game_id: number;
  connected_game_id: number;
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

const ignoreGameForUser = async (
  userId: number,
  gameId: number,
  sql: Sql = getSql()
) =>
  sql`
    insert into ignored_games (user_id, game_id)
    values (${userId}, ${gameId})
    on conflict (user_id, game_id)
    do update set updated_at = now()
  `;

const unignoreGameForUser = async (
  userId: number,
  gameId: number,
  sql: Sql = getSql()
) => sql`
  delete from ignored_games
  where user_id = ${userId}
    and game_id = ${gameId}
`;

const isGameIgnoredForUser = async (
  userId: number,
  gameId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql`
    select 1
    from ignored_games
    where user_id = ${userId}
      and game_id = ${gameId}
    limit 1
  `;
  return rows.length > 0;
};

const loadGameRatingForUser = async (
  userId: number,
  gameId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<GameRatingRow[]>`
    select game_id, rating
    from game_ratings
    where user_id = ${userId}
      and game_id = ${gameId}
    limit 1
  `;
  return rows[0] ?? null;
};

const upsertGameRatingForUser = async (
  userId: number,
  gameId: number,
  rating: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<GameRatingRow[]>`
    insert into game_ratings (user_id, game_id, rating)
    values (${userId}, ${gameId}, ${rating})
    on conflict (user_id, game_id)
    do update set rating = excluded.rating, updated_at = now()
    returning game_id, rating
  `;
  return rows[0] ?? null;
};

const deleteGameRatingForUser = async (
  userId: number,
  gameId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<{ game_id: number }[]>`
    delete from game_ratings
    where user_id = ${userId}
      and game_id = ${gameId}
    returning game_id
  `;
  return rows.length > 0;
};

const loadGameById = async (gameId: number, sql: Sql = getSql()) => {
  const rows = await sql<GameRow[]>`
    select g.id, g.title, p.key as platform, g.capsule_url
    from games g
    join platforms p on p.id = g.platform_id
    where g.id = ${gameId}
    limit 1
  `;
  return rows[0] ?? null;
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

const listGameConnections = async (userId: number, sql: Sql = getSql()) =>
  sql<GameConnectionRow[]>`
    select
      gc.game_id,
      g.title as game_title,
      gp.key as game_platform,
      g.capsule_url as game_capsule_url,
      gc.connected_game_id,
      cg.title as connected_title,
      cgp.key as connected_platform,
      cg.capsule_url as connected_capsule_url
    from game_connections gc
    join games g on g.id = gc.game_id
    join platforms gp on gp.id = g.platform_id
    join games cg on cg.id = gc.connected_game_id
    join platforms cgp on cgp.id = cg.platform_id
    where gc.user_id = ${userId}
    order by g.title, cg.title
  `;

const listGameConnectionEdges = async (userId: number, sql: Sql = getSql()) =>
  sql<GameConnectionEdgeRow[]>`
    select game_id, connected_game_id
    from game_connections
    where user_id = ${userId}
  `;

const insertGameConnection = async (
  userId: number,
  gameId: number,
  connectedGameId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<GameConnectionEdgeRow[]>`
    insert into game_connections (user_id, game_id, connected_game_id)
    values (${userId}, ${gameId}, ${connectedGameId})
    on conflict (user_id, game_id, connected_game_id)
    do update set updated_at = now()
    returning game_id, connected_game_id
  `;
  return rows[0] ?? null;
};

const deleteGameConnection = async (
  userId: number,
  gameId: number,
  connectedGameId: number,
  sql: Sql = getSql()
) => {
  const rows = await sql<GameConnectionEdgeRow[]>`
    delete from game_connections
    where user_id = ${userId}
      and game_id = ${gameId}
      and connected_game_id = ${connectedGameId}
    returning game_id, connected_game_id
  `;
  return rows[0] ?? null;
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
