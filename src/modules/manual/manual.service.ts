import { config } from "../../common/config";
import { AppError } from "../../common/errors/AppError";
import { normalizeGame, normalizeGameTitle } from "../games";
import { searchIgdbGames } from "../providers/clients/igdb-client";
import { manualRepository } from "./manual.repository";

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
  const existing = await manualRepository.loadProviderByPlatform(
    userId,
    "manual"
  );
  if (existing) {
    return existing;
  }

  const created = await manualRepository.createManualCredential(userId);
  if (!created?.[0]) {
    throw new AppError("Failed to create manual credential.", 500);
  }
  return created[0];
};

const ensureManualInput = async (userId: number, rawTitle: string) => {
  const canonicalTitle = normalizeGameTitle(rawTitle);
  const existing = await manualRepository.loadManualInputByCanonical(
    userId,
    canonicalTitle
  );
  if (existing) {
    await manualRepository.updateManualInputTitle(existing.id, rawTitle);
    return existing;
  }

  const created = await manualRepository.insertManualInput(
    userId,
    rawTitle,
    canonicalTitle
  );
  if (!created) {
    throw new AppError("Failed to create manual input.", 500);
  }
  return created;
};

export const manualService = {
  startManualSession: async (userId: number, title: string) => {
    const trimmed = title.trim();
    if (!trimmed) {
      throw new AppError("Title is required.", 400);
    }

    const credential = await getOrCreateManualCredential(userId);
    const manualInput = await ensureManualInput(userId, trimmed);
    const startedAt = new Date();

    await manualRepository.endActiveSessionsForCredential(
      credential.id,
      startedAt
    );

    let gameId = manualInput.mapped_game_id ?? null;
    if (!gameId) {
      const normalized = normalizeGame({
        platform: "manual",
        title: trimmed,
      });
      gameId = await manualRepository.ensureGameId(normalized);
    }

    const sessionId = await manualRepository.insertActivePlaySession(
      credential.id,
      gameId,
      startedAt
    );
    if (!sessionId) {
      throw new AppError("Failed to create manual session.", 500);
    }

    return {
      sessionId,
      gameId,
      platformCredentialId: credential.id,
      startedAt,
    };
  },

  createManualEntry: async (payload: {
    userId: number;
    startedAt: Date;
    durationSeconds: number;
    selection: ManualEntrySelection;
  }) => {
    if (
      !Number.isFinite(payload.durationSeconds) ||
      payload.durationSeconds <= 0
    ) {
      throw new AppError("Duration must be a positive number.", 400);
    }

    const credential = await getOrCreateManualCredential(payload.userId);

    const gameId =
      payload.selection.type === "local"
        ? payload.selection.gameId
        : await manualRepository.ensureGameId(
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

    const sessionId = await manualRepository.insertPlaySession(
      credential.id,
      gameId,
      payload.startedAt,
      endedAt
    );
    if (!sessionId) {
      throw new AppError("Failed to create manual entry.", 500);
    }

    return { sessionId };
  },

  stopManualSession: async (userId: number) => {
    const credential = await manualRepository.loadProviderByPlatform(
      userId,
      "manual"
    );
    if (!credential) {
      return { endedSessionCount: 0 };
    }

    const endedAt = new Date();
    const endedSessions = await manualRepository.endActiveSessionsForCredential(
      credential.id,
      endedAt
    );

    return { endedSessionCount: endedSessions.length };
  },

  getManualInputs: async (userId: number) => {
    const rows = await manualRepository.listManualInputs(userId);
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
  },

  mapManualInput: async (
    userId: number,
    inputId: number,
    selection: ManualMappingSelection
  ) => {
    let platformKey: string | null = null;
    if (selection.type === "igdb") {
      if (selection.platform) {
        platformKey = selection.platform;
      } else if (selection.platformId) {
        platformKey = await manualRepository.loadPlatformKeyByIgdbId(
          selection.platformId
        );
      }
    }

    const gameId =
      selection.type === "local"
        ? selection.gameId
        : await manualRepository.ensureIgdbGame({
            igdbId: selection.igdbId,
            title: selection.title,
            platform: platformKey,
            capsuleUrl: selection.coverUrl,
          });

    const updated = await manualRepository.updateManualInputMapping(
      userId,
      inputId,
      gameId
    );
    return updated;
  },

  searchManualMappingOptions: async (query: string) => {
    const [local, igdb] = await Promise.all([
      manualRepository.searchGamesByTitle(query, 5),
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
  },
};
