import type postgres from "postgres";

import {
  endPlaySession,
  ensureGameId,
  fetchActivePlaySession,
  isGameIgnoredForCredential,
  loadPlatformCredentials,
  recordPollResult,
  startPlaySession,
  storeResolvedAccountId,
} from "../../shared/db/queries";
import type { NormalizedGame } from "../../common/domain/game";

export type SqlClient = ReturnType<typeof postgres>;

export const pollingRepository = {
  fetchActivePlaySession: (
    platformCredentialId: number,
    sql?: SqlClient
  ) => fetchActivePlaySession(platformCredentialId, sql),

  endPlaySession: (sessionId: number, endedAt: Date, sql?: SqlClient) =>
    endPlaySession(sessionId, endedAt, sql),

  startPlaySession: (
    platformCredentialId: number,
    gameId: number,
    startedAt: Date,
    sql?: SqlClient
  ) => startPlaySession(platformCredentialId, gameId, startedAt, sql),

  ensureGameId: (game: NormalizedGame, sql?: SqlClient) =>
    ensureGameId(game, sql),

  isGameIgnoredForCredential: (
    platformCredentialId: number,
    gameId: number,
    sql?: SqlClient
  ) => isGameIgnoredForCredential(platformCredentialId, gameId, sql),

  recordPollResult: (
    platformCredentialId: number,
    gameId: number | null,
    polledAt: Date,
    rawPayload: unknown,
    sql?: SqlClient
  ) => recordPollResult(platformCredentialId, gameId, polledAt, rawPayload, sql),

  loadPlatformCredentials: () => loadPlatformCredentials(),

  storeResolvedAccountId: (credentialId: number, accountId: string) =>
    storeResolvedAccountId(credentialId, accountId),
};
