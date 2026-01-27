import {
  endActiveSessionsForCredential,
  ensureGameId,
  insertActivePlaySession,
  insertPlaySession,
  loadPlatformCredentialsByApiKey,
} from "../../shared/db/queries";
import type { NormalizedGame } from "../../common/domain/game";

export const playniteRepository = {
  loadCredentialsByApiKey: (apiKey: string, platform: string) =>
    loadPlatformCredentialsByApiKey(apiKey, platform),

  ensureGameId: (game: NormalizedGame) => ensureGameId(game),

  endActiveSessionsForCredential: (
    credentialId: number,
    endedAt: Date
  ) => endActiveSessionsForCredential(credentialId, endedAt),

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
};
