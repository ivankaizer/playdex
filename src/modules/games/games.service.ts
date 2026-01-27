import { AppError } from "../../common/errors/AppError";
import type {
  GameIgnoreResponseDto,
  GameRatingResponseDto,
} from "../../app/http/types";
import { gamesRepository } from "./games.repository";

const validateRating = (rating: number) => {
  if (!Number.isInteger(rating) || rating < 1 || rating > 5) {
    throw new AppError("rating must be an integer between 1 and 5.", 400);
  }
};

export const gamesService = {
  async getIgnoreStatus(
    userId: number,
    gameId: number
  ): Promise<GameIgnoreResponseDto> {
    const ignored = await gamesRepository.getIgnoreStatus(userId, gameId);
    return { gameId, ignored };
  },

  async ignoreGame(
    userId: number,
    gameId: number
  ): Promise<GameIgnoreResponseDto> {
    await gamesRepository.ignoreGame(userId, gameId);
    return { gameId, ignored: true };
  },

  async unignoreGame(
    userId: number,
    gameId: number
  ): Promise<GameIgnoreResponseDto> {
    await gamesRepository.unignoreGame(userId, gameId);
    return { gameId, ignored: false };
  },

  async getRating(
    userId: number,
    gameId: number
  ): Promise<GameRatingResponseDto> {
    const rating = await gamesRepository.getRating(userId, gameId);
    return { gameId, rating: rating?.rating ?? null };
  },

  async upsertRating(
    userId: number,
    gameId: number,
    rating: number
  ): Promise<GameRatingResponseDto> {
    validateRating(rating);
    const saved = await gamesRepository.upsertRating(userId, gameId, rating);
    return { gameId, rating: saved?.rating ?? rating };
  },

  async deleteRating(
    userId: number,
    gameId: number
  ): Promise<GameRatingResponseDto> {
    await gamesRepository.deleteRating(userId, gameId);
    return { gameId, rating: null };
  },
};

