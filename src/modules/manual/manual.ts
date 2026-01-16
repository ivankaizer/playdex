import {
  normalizeGame,
  normalizeGameTitle,
} from "../../shared/domain/normalize-game";
import {
  createManualCredential,
  endActiveSessionsForCredential,
  ensureGameId,
  ensureIgdbGame,
  insertActivePlaySession,
  insertPlaySession,
  insertManualInput,
  listManualInputs,
  loadPlatformKeyByIgdbId,
  loadManualInputByCanonical,
  loadProviderByPlatform,
  searchGamesByTitle,
  updateManualInputMapping,
  updateManualInputTitle,
} from "../../shared/db/queries";
import { searchIgdbGames } from "../providers/clients/igdb-client";
import { config } from "../../config/env";

type ManualMappingSelection =
  | { type: "local"; gameId: number }
  | {
      type: "igdb";
      igdbId: string;
      title: string;
      platform: string | null;
      platformId?: number | null;
      coverUrl: string | null;
    };

type ManualEntrySelection =
  | { type: "local"; gameId: number }
  | {
      type: "igdb";
      igdbId: string;
      title: string;
      platform: string;
      coverUrl: string | null;
    };

const getOrCreateManualCredential = async (userId: number) => {
  const existing = await loadProviderByPlatform(userId, "manual");
  if (existing) {
    return existing;
  }

  const created = await createManualCredential(userId);
  if (!created?.[0]) {
    throw new Error("Failed to create manual credential.");
  }
  return created[0];
};

const ensureManualInput = async (userId: number, rawTitle: string) => {
  const canonicalTitle = normalizeGameTitle(rawTitle);
  const existing = await loadManualInputByCanonical(userId, canonicalTitle);
  if (existing) {
    await updateManualInputTitle(existing.id, rawTitle);
    return existing;
  }

  const created = await insertManualInput(userId, rawTitle, canonicalTitle);
  if (!created) {
    throw new Error("Failed to create manual input.");
  }
  return created;
};

export const startManualSession = async (userId: number, title: string) => {
  const trimmed = title.trim();
  if (!trimmed) {
    throw new Error("Title is required.");
  }

  const credential = await getOrCreateManualCredential(userId);
  const manualInput = await ensureManualInput(userId, trimmed);
  const startedAt = new Date();

  await endActiveSessionsForCredential(credential.id, startedAt);

  let gameId = manualInput.mapped_game_id ?? null;
  if (!gameId) {
    const normalized = normalizeGame({
      platform: "manual",
      title: trimmed,
    });
    gameId = await ensureGameId(normalized);
  }

  const sessionId = await insertActivePlaySession(
    credential.id,
    gameId,
    startedAt
  );
  if (!sessionId) {
    throw new Error("Failed to create manual session.");
  }

  return {
    sessionId,
    gameId,
    platformCredentialId: credential.id,
    startedAt,
  };
};

export const createManualEntry = async (payload: {
  userId: number;
  startedAt: Date;
  durationSeconds: number;
  selection: ManualEntrySelection;
}) => {
  if (
    !Number.isFinite(payload.durationSeconds) ||
    payload.durationSeconds <= 0
  ) {
    throw new Error("Duration must be a positive number.");
  }

  const credential = await getOrCreateManualCredential(payload.userId);

  const gameId =
    payload.selection.type === "local"
      ? payload.selection.gameId
      : await ensureGameId(
          normalizeGame({
            platform: payload.selection.platform,
            title: payload.selection.title,
            externalId: payload.selection.igdbId,
            capsuleUrl: payload.selection.coverUrl ?? undefined,
          })
        );

  const endedAt = new Date(
    payload.startedAt.getTime() + payload.durationSeconds * 1000
  );

  const sessionId = await insertPlaySession(
    credential.id,
    gameId,
    payload.startedAt,
    endedAt
  );
  if (!sessionId) {
    throw new Error("Failed to create manual entry.");
  }

  return { sessionId };
};

export const stopManualSession = async (userId: number) => {
  const credential = await loadProviderByPlatform(userId, "manual");
  if (!credential) {
    return { endedSessionCount: 0 };
  }

  const endedAt = new Date();
  const endedSessions = await endActiveSessionsForCredential(
    credential.id,
    endedAt
  );

  return { endedSessionCount: endedSessions.length };
};

export const getManualInputs = async (userId: number) => {
  const rows = await listManualInputs(userId);
  return rows.map((row) => ({
    id: row.id,
    input: row.raw_title,
    canonicalTitle: row.canonical_title,
    mappedGameId: row.mapped_game_id,
    mappedSource: row.mapped_canonical_key?.startsWith("igdb:")
      ? "igdb"
      : row.mapped_game_id
        ? "local"
        : null,
    mappedGame: row.mapped_game_id
      ? {
          id: row.mapped_game_id,
          title: row.mapped_title ?? "",
          platform: row.mapped_platform ?? "",
          capsuleUrl: row.mapped_capsule_url,
        }
      : null,
  }));
};

export const mapManualInput = async (
  userId: number,
  inputId: number,
  selection: ManualMappingSelection
) => {
  let platformKey: string | null = null;
  if (selection.type === "igdb") {
    if (selection.platform) {
      platformKey = selection.platform;
    } else if (selection.platformId) {
      platformKey = await loadPlatformKeyByIgdbId(selection.platformId);
    }
  }

  const gameId =
    selection.type === "local"
      ? selection.gameId
      : await ensureIgdbGame({
          igdbId: selection.igdbId,
          title: selection.title,
          platform: platformKey,
          capsuleUrl: selection.coverUrl,
        });

  const updated = await updateManualInputMapping(userId, inputId, gameId);
  if (!updated) {
    return null;
  }
  return updated;
};

export const searchManualMappingOptions = async (query: string) => {
  const [local, igdb] = await Promise.all([
    searchGamesByTitle(query, 5),
    config.igdbClientId && config.igdbClientSecret
      ? searchIgdbGames(query, {
          clientId: config.igdbClientId,
          clientSecret: config.igdbClientSecret,
        })
      : [],
  ]);

  return {
    local: local.map((game) => ({
      source: "local" as const,
      gameId: game.id,
      title: game.title,
      platform: game.platform,
      capsuleUrl: game.capsule_url,
    })),
    igdb: igdb.map((game) => ({
      source: "igdb" as const,
      igdbId: game.id,
      title: game.title,
      platform: game.platform,
      platformId: game.platformId,
      capsuleUrl: game.coverUrl,
    })),
  };
};
