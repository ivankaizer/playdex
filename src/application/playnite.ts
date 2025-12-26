import { normalizeGame } from "../domain/normalize-game";
import {
  ensureGameId,
  endActiveSessionsForCredential,
  insertActivePlaySession,
  insertPlaySession,
  loadPlatformCredentialsByApiKey,
} from "../infrastructure/db/queries";

export type PlayniteGameProvider = "steam" | "unknown";

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
  endedSessions: { id: number; gameId: number }[];
};

export type PlayniteSessionEndInput = {
  apiKey: string;
};

export type PlayniteSessionEndResult = {
  platformCredentialId: number;
  endedAt: Date;
  endedSessions: { id: number; gameId: number }[];
};

export const recordPlayniteSession = async (
  input: PlayniteSessionInput
): Promise<PlayniteSessionResult | null> => {
  const credentials = await loadPlatformCredentialsByApiKey(
    input.apiKey,
    "playnite"
  );

  if (credentials.length !== 1) {
    return null;
  }

  const normalized = normalizeGame({
    platform: input.gameProvider,
    title: input.gameTitle,
    externalId: input.gameId,
  });
  const gameId = await ensureGameId(normalized);
  const endedAt = new Date();
  const startedAt = new Date(
    endedAt.getTime() - input.durationInSeconds * 1000
  );

  const sessionId = await insertPlaySession(
    credentials[0].id,
    gameId,
    startedAt,
    endedAt
  );

  if (!sessionId) {
    return null;
  }

  return { sessionId };
};

export const startPlayniteSession = async (
  input: PlayniteSessionStartInput
): Promise<PlayniteSessionStartResult | null> => {
  const credentials = await loadPlatformCredentialsByApiKey(
    input.apiKey,
    "playnite"
  );

  if (credentials.length !== 1) {
    return null;
  }

  const normalized = normalizeGame({
    platform: input.gameProvider,
    title: input.gameTitle,
    externalId: input.gameId,
  });
  const gameId = await ensureGameId(normalized);
  const startedAt = new Date();

  const endedSessions = await endActiveSessionsForCredential(
    credentials[0].id,
    startedAt
  );

  const sessionId = await insertActivePlaySession(
    credentials[0].id,
    gameId,
    startedAt
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
    })),
  };
};

export const endPlayniteSessions = async (
  input: PlayniteSessionEndInput
): Promise<PlayniteSessionEndResult | null> => {
  const credentials = await loadPlatformCredentialsByApiKey(
    input.apiKey,
    "playnite"
  );

  if (credentials.length !== 1) {
    return null;
  }

  const endedAt = new Date();
  const endedSessions = await endActiveSessionsForCredential(
    credentials[0].id,
    endedAt
  );

  return {
    platformCredentialId: credentials[0].id,
    endedAt,
    endedSessions: endedSessions.map((session) => ({
      id: session.id,
      gameId: session.game_id,
    })),
  };
};
