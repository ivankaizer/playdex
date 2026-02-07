import type postgres from "postgres";

import { getSql } from "../../shared/db/postgres";

export type SqlClient = ReturnType<typeof postgres>;

type NotificationTargetRow = {
  user_id: number;
  chat_id: string | null;
  platform: string;
};

type GameRow = {
  id: number;
  title: string;
  platform: string;
  capsule_url: string | null;
};

type UserChatRow = {
  id: number;
  email: string;
  chat_id: string | null;
};

const loadNotificationTargetByCredential = async (
  platformCredentialId: number,
  sql: SqlClient = getSql()
) => {
  const rows = await sql<NotificationTargetRow[]>`
    select u.id as user_id, u.chat_id, p.key as platform
    from platform_credentials pc
    join users u on u.id = pc.user_id
    join platforms p on p.id = pc.platform_id
    where pc.id = ${platformCredentialId}
    limit 1
  `;
  return rows[0] ?? null;
};

const loadGameById = async (gameId: number, sql: SqlClient = getSql()) => {
  const rows = await sql<GameRow[]>`
    select g.id, g.title, p.key as platform, g.capsule_url
    from games g
    join platforms p on p.id = g.platform_id
    where g.id = ${gameId}
    limit 1
  `;
  return rows[0] ?? null;
};

const loadUserByChatId = async (chatId: string, sql: SqlClient = getSql()) => {
  const rows = await sql<UserChatRow[]>`
    select id, email, chat_id
    from users
    where chat_id = ${chatId}
    limit 1
  `;
  return rows[0] ?? null;
};

const endActiveSessionsForUser = async (
  userId: number,
  endedAt: Date,
  sql: SqlClient = getSql()
) =>
  sql<{ id: number; platform: string; title: string }[]>`
    update play_sessions as ps
    set ended_at = ${endedAt}, updated_at = now()
    from platform_credentials pc, games g, platforms p
    where ps.platform_credential_id = pc.id
      and g.id = ps.game_id
      and p.id = pc.platform_id
      and pc.user_id = ${userId}
      and ps.ended_at is null
    returning ps.id, p.key as platform, g.title
  `;

export const telegramRepository = {
  loadNotificationTargetByCredential: (
    platformCredentialId: number,
    sql: SqlClient
  ) => loadNotificationTargetByCredential(platformCredentialId, sql),
  loadGameById: (gameId: number, sql: SqlClient) => loadGameById(gameId, sql),
  loadUserByChatId: (chatId: string, sql: SqlClient) =>
    loadUserByChatId(chatId, sql),
  endActiveSessionsForUser: (userId: number, endedAt: Date, sql: SqlClient) =>
    endActiveSessionsForUser(userId, endedAt, sql),
};
