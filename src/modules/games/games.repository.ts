import {
  deleteGameRatingForUser,
  deleteGameConnection,
  ignoreGameForUser,
  isGameIgnoredForUser,
  listGameConnectionEdges,
  listGameConnections,
  loadGameRatingForUser,
  loadGameById,
  searchGamesByTitle,
  insertGameConnection,
  unignoreGameForUser,
  upsertGameRatingForUser,
} from "../../shared/db/queries";

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
