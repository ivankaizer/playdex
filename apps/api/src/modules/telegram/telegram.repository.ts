import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";

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

const normalizeNotificationTargetRow = (
  row: Omit<NotificationTargetRow, "user_id"> & { user_id: string }
): NotificationTargetRow => ({
  ...row,
  user_id: toDomainId(row.user_id, "users.id"),
});

const normalizeGameRow = (
  row: Omit<GameRow, "id"> & { id: string }
): GameRow => ({
  ...row,
  id: toDomainId(row.id, "games.id"),
});

const normalizeUserChatRow = (
  row: Omit<UserChatRow, "id"> & { id: string }
): UserChatRow => ({
  ...row,
  id: toDomainId(row.id, "users.id"),
});

const loadNotificationTargetByCredential = async (
  platformCredentialId: number,
  db: DbExecutor = getDb()
) => {
  const row = await db
    .selectFrom("platform_credentials as pc")
    .innerJoin("users as u", "u.id", "pc.user_id")
    .innerJoin("platforms as p", "p.id", "pc.platform_id")
    .select(["u.id as user_id", "u.chat_id", "p.key as platform"])
    .where("pc.id", "=", String(platformCredentialId))
    .executeTakeFirst();

  return row ? normalizeNotificationTargetRow(row) : null;
};

const loadGameById = async (gameId: number, db: DbExecutor = getDb()) => {
  const row = await db
    .selectFrom("games as g")
    .innerJoin("platforms as p", "p.id", "g.platform_id")
    .select(["g.id", "g.title", "p.key as platform", "g.capsule_url"])
    .where("g.id", "=", String(gameId))
    .executeTakeFirst();

  return row ? normalizeGameRow(row) : null;
};

const loadUserByChatId = async (chatId: string, db: DbExecutor = getDb()) => {
  const row = await db
    .selectFrom("users")
    .select(["id", "email", "chat_id"])
    .where("chat_id", "=", chatId)
    .executeTakeFirst();

  return row ? normalizeUserChatRow(row) : null;
};

const endActiveSessionsForUser = async (
  userId: number,
  endedAt: Date,
  db: DbExecutor = getDb()
) => {
  const rows = await db
    .updateTable("play_sessions as ps")
    .set((eb) => ({ ended_at: endedAt, updated_at: eb.fn<Date>("now") }))
    .from(["platform_credentials as pc", "games as g", "platforms as p"])
    .whereRef("ps.platform_credential_id", "=", "pc.id")
    .whereRef("g.id", "=", "ps.game_id")
    .whereRef("p.id", "=", "pc.platform_id")
    .where("pc.user_id", "=", String(userId))
    .where("ps.ended_at", "is", null)
    .returning(["ps.id", "p.key as platform", "g.title"])
    .execute();

  return rows.map((row) => ({
    ...row,
    id: toDomainId(row.id, "play_sessions.id"),
  }));
};

export const telegramRepository = {
  loadNotificationTargetByCredential: (
    platformCredentialId: number,
    db: DbExecutor
  ) => loadNotificationTargetByCredential(platformCredentialId, db),
  loadGameById: (gameId: number, db: DbExecutor) => loadGameById(gameId, db),
  loadUserByChatId: (chatId: string, db: DbExecutor) =>
    loadUserByChatId(chatId, db),
  endActiveSessionsForUser: (userId: number, endedAt: Date, db: DbExecutor) =>
    endActiveSessionsForUser(userId, endedAt, db),
};
