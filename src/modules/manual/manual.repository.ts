import {
  createManualCredential,
  endActiveSessionsForCredential,
  ensureGameId,
  ensureIgdbGame,
  insertActivePlaySession,
  insertManualInput,
  insertPlaySession,
  listManualInputs,
  loadManualInputByCanonical,
  loadPlatformKeyByIgdbId,
  loadProviderByPlatform,
  searchGamesByTitle,
  updateManualInputMapping,
  updateManualInputTitle,
} from "../../shared/db/queries";
import type { NormalizedGame } from "../../common/domain/game";

export const manualRepository = {
  loadProviderByPlatform: (userId: number, platform: string) =>
    loadProviderByPlatform(userId, platform),

  createManualCredential: (userId: number) => createManualCredential(userId),

  endActiveSessionsForCredential: (
    credentialId: number,
    endedAt: Date
  ) => endActiveSessionsForCredential(credentialId, endedAt),

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

  updateManualInputMapping: (
    userId: number,
    inputId: number,
    gameId: number
  ) => updateManualInputMapping(userId, inputId, gameId),

  listManualInputs: (userId: number) => listManualInputs(userId),

  loadPlatformKeyByIgdbId: (platformId: number) =>
    loadPlatformKeyByIgdbId(platformId),

  searchGamesByTitle: (title: string, limit: number) =>
    searchGamesByTitle(title, limit),
};
