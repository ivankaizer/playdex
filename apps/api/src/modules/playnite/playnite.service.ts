import { normalizeGame } from "../games";
import { getDb, type DbExecutor } from "../../common/db/database";
import { playniteRepository } from "./playnite.repository";

export type PlayniteGameProvider = "steam" | "unknown";
export const PLAYNITE_PING_TIMEOUT_MINUTES = 3;

export type PlayniteSessionInput = {
  apiKey: string;
  durationInSeconds: number;
  gameTitle: string;
  gameId: string;
  gameProvider: PlayniteGameProvider;
};

export type PlayniteSessionResult = {
  sessionId: number;
};

export type PlayniteSessionStartInput = Omit<
  PlayniteSessionInput,
  "durationInSeconds"
>;

export type PlayniteSessionStartResult = {
  sessionId: number;
  platformCredentialId: number;
  gameId: number;
  startedAt: Date;
  endedSessions: { id: number; gameId: number; startedAt: Date }[];
};

export type PlayniteSessionEndInput = {
  apiKey: string;
};

export type PlayniteSessionEndResult = {
  platformCredentialId: number;
  endedAt: Date;
  endedSessions: { id: number; gameId: number; startedAt: Date }[];
};

export type PlayniteSessionPingInput = Omit<
  PlayniteSessionInput,
  "durationInSeconds"
>;

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
  recordSession: async (
    input: PlayniteSessionInput,
    db = getDb()
  ): Promise<PlayniteSessionResult | null> => {
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
      const startedAt = new Date(
        endedAt.getTime() - input.durationInSeconds * 1000
      );

      const sessionId = await playniteRepository.insertPlaySession(
        credentials[0].id,
        gameId,
        startedAt,
        endedAt,
        trx
      );

      return sessionId ? { sessionId } : null;
    });
  },

  startSession: async (
    input: PlayniteSessionStartInput,
    db = getDb()
  ): Promise<PlayniteSessionStartResult | null> => {
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
      const startedAt = new Date();

      const endedSessions =
        await playniteRepository.endActiveSessionsForCredential(
          credentials[0].id,
          startedAt,
          trx
        );

      const sessionId = await playniteRepository.insertActivePlaySession(
        credentials[0].id,
        gameId,
        startedAt,
        trx
      );

      if (!sessionId) {
        return null;
      }

      return {
        sessionId,
        platformCredentialId: credentials[0].id,
        gameId,
        startedAt,
        endedSessions: endedSessions.map((session) => ({
          id: session.id,
          gameId: session.game_id,
          startedAt: session.started_at,
        })),
      };
    });
  },

  endSessions: async (
    input: PlayniteSessionEndInput,
    db: DbExecutor = getDb()
  ): Promise<PlayniteSessionEndResult | null> => {
    const credentials = await playniteRepository.loadCredentialsByApiKey(
      input.apiKey,
      "playnite",
      db
    );

    if (credentials.length !== 1) {
      return null;
    }

    const endedAt = new Date();
    const endedSessions =
      await playniteRepository.endActiveSessionsForCredential(
        credentials[0].id,
        endedAt,
        db
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

      const endedMismatchedSessions =
        await playniteRepository.endActiveSessionsForCredentialExceptGame(
          platformCredentialId,
          gameId,
          now,
          trx
        );

      const activeSession =
        await playniteRepository.fetchActiveSessionForCredential(
          platformCredentialId,
          trx
        );

      if (activeSession && activeSession.game_id === gameId) {
        await playniteRepository.touchActiveSession(activeSession.id, now, trx);
        return {
          sessionId: activeSession.id,
          sessionStarted: false,
          platformCredentialId,
          gameId,
          startedAt: activeSession.started_at,
          endedSessions: [
            ...endedTimedOutSessions,
            ...endedMismatchedSessions,
          ].map((session) => ({
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
        endedSessions: [
          ...endedTimedOutSessions,
          ...endedMismatchedSessions,
        ].map((session) => ({
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
