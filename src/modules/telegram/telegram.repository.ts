import type postgres from "postgres";

import {
  endActiveSessionsForUser,
  loadGameById,
  loadNotificationTargetByCredential,
  loadUserByChatId,
} from "../../shared/db/queries";

export type SqlClient = ReturnType<typeof postgres>;

export const telegramRepository = {
  loadNotificationTargetByCredential: (platformCredentialId: number, sql: SqlClient) =>
    loadNotificationTargetByCredential(platformCredentialId, sql),
  loadGameById: (gameId: number, sql: SqlClient) => loadGameById(gameId, sql),
  loadUserByChatId: (chatId: string, sql: SqlClient) =>
    loadUserByChatId(chatId, sql),
  endActiveSessionsForUser: (userId: number, endedAt: Date, sql: SqlClient) =>
    endActiveSessionsForUser(userId, endedAt, sql),
};

