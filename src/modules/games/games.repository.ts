import {
  deleteGameRatingForUser,
  ignoreGameForUser,
  isGameIgnoredForUser,
  loadGameRatingForUser,
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
};

