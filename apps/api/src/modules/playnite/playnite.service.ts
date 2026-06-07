import { normalizeGame } from "../games";
import { getDb, type DbExecutor } from "../../common/db/database";
import { playniteRepository } from "./playnite.repository";

export type PlayniteGameProvider = "steam" | "unknown";
export const PLAYNITE_PING_TIMEOUT_MINUTES = 3;

export type PlayniteSessionEndInput = {
  apiKey: string;
  gameTitle: string;
  gameId: string;
  gameProvider: PlayniteGameProvider;
};

export type PlayniteSessionEndResult = {
  platformCredentialId: number;
  endedAt: Date;
  endedSessions: { id: number; gameId: number; startedAt: Date }[];
};

export type PlayniteSessionPingInput = PlayniteSessionEndInput;

export type PlayniteSessionPingResult = {
  sessionId: number;
  sessionStarted: boolean;
  platformCredentialId: number;
  gameId: number;
  startedAt: Date;
  endedSessions: {
    id: number;
    platformCredentialId: number;
    gameId: number;
    startedAt: Date;
    endedAt: Date;
  }[];
};

export type PlayniteTimedOutSession = {
  id: number;
  platformCredentialId: number;
  gameId: number;
  startedAt: Date;
  endedAt: Date;
};

export const playniteService = {
  endSessions: async (
    input: PlayniteSessionEndInput,
    db = getDb()
  ): Promise<PlayniteSessionEndResult | null> => {
    return db.transaction().execute(async (trx) => {
      const credentials = await playniteRepository.loadCredentialsByApiKey(
        input.apiKey,
        "playnite",
        trx
      );

      if (credentials.length !== 1) {
        return null;
      }

      const normalized = normalizeGame({
        platform: input.gameProvider,
        title: input.gameTitle,
        externalId: input.gameId,
      });
      const gameId = await playniteRepository.ensureGameId(normalized, trx);
      const endedAt = new Date();
      const endedSessions =
        await playniteRepository.endActiveSessionsForCredentialAndGame(
          credentials[0].id,
          gameId,
          endedAt,
          trx
        );

      return {
        platformCredentialId: credentials[0].id,
        endedAt,
        endedSessions: endedSessions.map((session) => ({
          id: session.id,
          gameId: session.game_id,
          startedAt: session.started_at,
        })),
      };
    });
  },

  pingSession: async (
    input: PlayniteSessionPingInput,
    db = getDb()
  ): Promise<PlayniteSessionPingResult | null> => {
    return db.transaction().execute(async (trx) => {
      const credentials = await playniteRepository.loadCredentialsByApiKey(
        input.apiKey,
        "playnite",
        trx
      );

      if (credentials.length !== 1) {
        return null;
      }

      const platformCredentialId = credentials[0].id;
      const normalized = normalizeGame({
        platform: input.gameProvider,
        title: input.gameTitle,
        externalId: input.gameId,
      });
      const gameId = await playniteRepository.ensureGameId(normalized, trx);
      const now = new Date();
      const timeoutBefore = new Date(
        now.getTime() - PLAYNITE_PING_TIMEOUT_MINUTES * 60_000
      );

      const endedTimedOutSessions =
        await playniteRepository.endTimedOutActiveSessionsForCredential(
          platformCredentialId,
          timeoutBefore,
          now,
          PLAYNITE_PING_TIMEOUT_MINUTES,
          trx
        );

      const activeSession =
        await playniteRepository.fetchActiveSessionForCredentialAndGame(
          platformCredentialId,
          gameId,
          trx
        );

      if (activeSession) {
        await playniteRepository.touchActiveSession(activeSession.id, now, trx);
        return {
          sessionId: activeSession.id,
          sessionStarted: false,
          platformCredentialId,
          gameId,
          startedAt: activeSession.started_at,
          endedSessions: endedTimedOutSessions.map((session) => ({
            id: session.id,
            platformCredentialId: session.platform_credential_id,
            gameId: session.game_id,
            startedAt: session.started_at,
            endedAt: session.ended_at,
          })),
        };
      }

      const sessionId = await playniteRepository.insertActivePlaySession(
        platformCredentialId,
        gameId,
        now,
        trx
      );

      if (!sessionId) {
        return null;
      }

      return {
        sessionId,
        sessionStarted: true,
        platformCredentialId,
        gameId,
        startedAt: now,
        endedSessions: endedTimedOutSessions.map((session) => ({
          id: session.id,
          platformCredentialId: session.platform_credential_id,
          gameId: session.game_id,
          startedAt: session.started_at,
          endedAt: session.ended_at,
        })),
      };
    });
  },

  endTimedOutSessions: async (
    db: DbExecutor = getDb()
  ): Promise<PlayniteTimedOutSession[]> => {
    const now = new Date();
    const timeoutBefore = new Date(
      now.getTime() - PLAYNITE_PING_TIMEOUT_MINUTES * 60_000
    );
    const endedSessions = await playniteRepository.endTimedOutActiveSessions(
      timeoutBefore,
      now,
      PLAYNITE_PING_TIMEOUT_MINUTES,
      db
    );

    return endedSessions.map((session) => ({
      id: session.id,
      platformCredentialId: session.platform_credential_id,
      gameId: session.game_id,
      startedAt: session.started_at,
      endedAt: session.ended_at,
    }));
  },
};
