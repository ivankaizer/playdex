import { AppError } from "../../common/errors/AppError";
import type {
  GameIgnoreResponseDto,
  GameConnectionChangeResponseDto,
  GameConnectionListResponseDto,
  GameResponseDto,
  GameSearchResponseDto,
  GameRatingResponseDto,
} from "../../app/http/types";
import { gamesRepository } from "./games.repository";
import {
  normalizeGameConnection,
  type GameConnectionEdge,
} from "./gameConnections";
import { encodeGameId } from "../../common/utils/gameHash";

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
    return { gameId: encodeGameId(gameId), ignored };
  },

  async ignoreGame(
    userId: number,
    gameId: number
  ): Promise<GameIgnoreResponseDto> {
    await gamesRepository.ignoreGame(userId, gameId);
    return { gameId: encodeGameId(gameId), ignored: true };
  },

  async unignoreGame(
    userId: number,
    gameId: number
  ): Promise<GameIgnoreResponseDto> {
    await gamesRepository.unignoreGame(userId, gameId);
    return { gameId: encodeGameId(gameId), ignored: false };
  },

  async getRating(
    userId: number,
    gameId: number
  ): Promise<GameRatingResponseDto> {
    const rating = await gamesRepository.getRating(userId, gameId);
    return { gameId: encodeGameId(gameId), rating: rating?.rating ?? null };
  },

  async upsertRating(
    userId: number,
    gameId: number,
    rating: number
  ): Promise<GameRatingResponseDto> {
    validateRating(rating);
    const saved = await gamesRepository.upsertRating(userId, gameId, rating);
    return { gameId: encodeGameId(gameId), rating: saved?.rating ?? rating };
  },

  async deleteRating(
    userId: number,
    gameId: number
  ): Promise<GameRatingResponseDto> {
    await gamesRepository.deleteRating(userId, gameId);
    return { gameId: encodeGameId(gameId), rating: null };
  },

  async getGameById(userId: number, gameId: number): Promise<GameResponseDto> {
    const game = await gamesRepository.getGameById(gameId);
    if (!game) {
      throw new AppError("Game not found.", 404);
    }
    return {
      id: encodeGameId(Number(game.id)),
      title: game.title,
      platform: game.platform,
      capsuleUrl: game.capsule_url ?? null,
    };
  },

  async searchGames(
    userId: number,
    query: string,
    limit = 10
  ): Promise<GameSearchResponseDto> {
    const trimmed = query.trim();
    if (!trimmed) {
      throw new AppError("query is required.", 400);
    }
    const results = await gamesRepository.searchGames(trimmed, limit);
    return {
      query: trimmed,
      results: results.map((game) => ({
        gameId: encodeGameId(Number(game.id)),
        title: game.title,
        platform: game.platform,
        capsuleUrl: game.capsule_url ?? null,
      })),
    };
  },

  async listConnections(
    userId: number
  ): Promise<GameConnectionListResponseDto> {
    const rows = await gamesRepository.listGameConnections(userId);
    return {
      connections: rows.map((row) => ({
        gameId: encodeGameId(Number(row.game_id)),
        title: row.game_title,
        platform: row.game_platform,
        capsuleUrl: row.game_capsule_url ?? null,
        connectedGameId: encodeGameId(Number(row.connected_game_id)),
        connectedTitle: row.connected_title,
        connectedPlatform: row.connected_platform,
        connectedCapsuleUrl: row.connected_capsule_url ?? null,
      })),
    };
  },

  async listConnectionEdges(userId: number): Promise<GameConnectionEdge[]> {
    const rows = await gamesRepository.listConnectionEdges(userId);
    return rows.map((row) => ({
      game_id: Number(row.game_id),
      connected_game_id: Number(row.connected_game_id),
    }));
  },

  async connectGames(
    userId: number,
    gameId: number,
    connectedGameId: number
  ): Promise<GameConnectionChangeResponseDto> {
    const normalized = normalizeGameConnection(gameId, connectedGameId);
    if (!normalized) {
      throw new AppError("gameId and connectedGameId must be different.", 400);
    }
    const [sourceId, targetId] = normalized;
    const [source, target] = await Promise.all([
      gamesRepository.getGameById(sourceId),
      gamesRepository.getGameById(targetId),
    ]);
    if (!source || !target) {
      throw new AppError("Both games must exist to connect.", 404);
    }

    await gamesRepository.createGameConnection(userId, sourceId, targetId);
    return {
      gameId: encodeGameId(sourceId),
      connectedGameId: encodeGameId(targetId),
    };
  },

  async disconnectGames(
    userId: number,
    gameId: number,
    connectedGameId: number
  ): Promise<GameConnectionChangeResponseDto> {
    const normalized = normalizeGameConnection(gameId, connectedGameId);
    if (!normalized) {
      throw new AppError("gameId and connectedGameId must be different.", 400);
    }
    const [sourceId, targetId] = normalized;
    await gamesRepository.deleteGameConnection(userId, sourceId, targetId);
    return {
      gameId: encodeGameId(sourceId),
      connectedGameId: encodeGameId(targetId),
    };
  },
};
