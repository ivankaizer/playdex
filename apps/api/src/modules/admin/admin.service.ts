import { AppError } from "../../common/errors/AppError";
import { encodeGameId } from "../../common/utils/gameHash";
import { parseGameId } from "../../common/utils/gameId";
import type {
  AdminCredentialDto,
  AdminGameDto,
  AdminPlatformStatusDto,
  AdminRecentSessionsResponseDto,
  AdminSessionsListResponseDto,
} from "../../app/http/types";
import { adminRepository } from "./admin.repository";

const mapCredential = (row: {
  id: number;
  user_email: string;
  platform: string;
  api_key: string | null;
  account_id: string | null;
  active: boolean;
}): AdminCredentialDto => ({
  id: row.id,
  userEmail: row.user_email,
  platform: row.platform,
  apiKey: row.api_key,
  user: row.account_id,
  active: row.active,
});

const mapGame = (row: {
  id: number | string;
  title: string;
  title_locked: boolean;
  platform: string;
  capsule_url: string | null;
  capsule_url_locked: boolean;
}): AdminGameDto => {
  const numericId = Number(row.id);
  if (!Number.isInteger(numericId) || numericId <= 0) {
    throw new AppError("Invalid game id returned from database.", 500);
  }

  return {
    id: encodeGameId(numericId),
    title: row.title,
    titleLocked: row.title_locked,
    platform: row.platform,
    imageUrl: row.capsule_url ?? null,
    imageLocked: row.capsule_url_locked,
  };
};

export const adminService = {
  listRecentSessions: async (): Promise<AdminRecentSessionsResponseDto> => {
    const rows = await adminRepository.listRecentSessions();
    const users = new Map<
      number,
      {
        userId: number;
        email: string;
        sessions: {
          sessionId: number;
          gameName: string;
          startedAt: Date;
          durationSeconds: number;
          live: boolean;
        }[];
      }
    >();

    for (const row of rows) {
      if (!users.has(row.user_id)) {
        users.set(row.user_id, {
          userId: row.user_id,
          email: row.email,
          sessions: [],
        });
      }
      const current = users.get(row.user_id);
      if (current) {
        current.sessions.push({
          sessionId: row.session_id,
          gameName: row.game_name,
          startedAt: row.started_at,
          durationSeconds: row.duration_seconds,
          live: row.live,
        });
      }
    }

    return { users: Array.from(users.values()) };
  },

  listSessions: async (
    query: string,
    limit: number,
    offset: number
  ): Promise<AdminSessionsListResponseDto> => {
    const trimmedQuery = query.trim();
    const safeLimit = Math.min(Math.max(limit, 1), 200);
    const safeOffset = Math.max(0, Math.floor(offset));
    const [rows, total] = await Promise.all([
      adminRepository.listSessions(trimmedQuery, safeLimit, safeOffset),
      adminRepository.countSessions(trimmedQuery),
    ]);
    return {
      query: trimmedQuery,
      sessions: rows.map((row) => ({
        sessionId: row.session_id,
        sourceCredentialId: row.source_credential_id,
        userEmail: row.user_email,
        platform: row.platform,
        accountUser: row.account_user,
        gameName: row.game_name,
        startedAt: row.started_at,
        endedAt: row.ended_at,
        durationSeconds: row.duration_seconds,
        live: row.live,
      })),
      total,
      limit: safeLimit,
      offset: safeOffset,
    };
  },

  listPlatformStatuses: async () => {
    const rows = await adminRepository.listPlatformStatuses();
    const platforms: AdminPlatformStatusDto[] = rows.map((row) => ({
      platform: row.platform,
      totalCredentials: row.total_credentials,
      activeCredentials: row.active_credentials,
      enabled: row.active_credentials > 0,
    }));
    return { platforms };
  },

  updatePlatformStatus: async (platform: string, enabled: boolean) => {
    const existing = await adminRepository.loadPlatformByKey(platform);
    if (!existing) {
      throw new AppError("Platform not found.", 404);
    }

    const updatedCredentials =
      await adminRepository.updatePlatformCredentialsActive(platform, enabled);

    return { platform, enabled, updatedCredentials };
  },

  listCredentials: async () => {
    const rows = await adminRepository.listCredentials();
    return { credentials: rows.map(mapCredential) };
  },

  moveSessions: async (sessionIds: number[], targetCredentialId: number) => {
    if (sessionIds.length === 0) {
      throw new AppError("sessionIds must contain at least one session.", 400);
    }

    const target = await adminRepository.loadCredentialById(targetCredentialId);
    if (!target) {
      throw new AppError("Target credential not found.", 404);
    }

    const movedSessions = await adminRepository.moveSessions(
      sessionIds,
      targetCredentialId
    );
    return { movedSessions, targetCredentialId };
  },

  updateCredential: async (
    credentialId: number,
    changes: {
      apiKeyChanged: boolean;
      apiKey: string | null;
      userChanged: boolean;
      user: string | null;
    }
  ) => {
    if (!changes.apiKeyChanged && !changes.userChanged) {
      throw new AppError("At least one field must be provided.", 400);
    }

    const updated = await adminRepository.updateCredential(
      credentialId,
      changes
    );
    if (!updated) {
      throw new AppError("Credential not found.", 404);
    }

    return { credential: mapCredential(updated) };
  },

  searchGames: async (query: string, limit: number) => {
    const trimmedQuery = query.trim();
    const safeLimit = Math.min(Math.max(limit, 1), 50);
    let parsedGameId: number | null = null;
    try {
      parsedGameId = parseGameId(trimmedQuery);
    } catch {
      // Query can be plain text (title/image), so a parse failure is expected.
    }
    const rows = await adminRepository.searchGames(
      trimmedQuery,
      parsedGameId,
      safeLimit
    );
    return { query: trimmedQuery, games: rows.map(mapGame) };
  },

  updateGame: async (
    gameId: number,
    changes: {
      titleChanged: boolean;
      title: string;
      lockTitleChanged: boolean;
      lockTitle: boolean;
      imageUrlChanged: boolean;
      imageUrl: string | null;
      lockImageChanged: boolean;
      lockImage: boolean;
    }
  ) => {
    if (
      !changes.titleChanged &&
      !changes.lockTitleChanged &&
      !changes.imageUrlChanged &&
      !changes.lockImageChanged
    ) {
      throw new AppError("At least one field must be provided.", 400);
    }

    const updated = await adminRepository.updateGame(gameId, changes);
    if (!updated) {
      throw new AppError("Game not found.", 404);
    }

    return { game: mapGame(updated) };
  },
};
