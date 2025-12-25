import { normalizeGame } from "../domain/normalize-game";
import {
  ensureGameId,
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
