import { getDb, type DbExecutor } from "../../common/db/database";
import { toDomainId } from "../../common/db/ids";

type UserAuthRow = {
  id: number;
  email: string;
  password: string | null;
  is_admin: boolean;
  chat_id?: string | null;
};

const normalizeUserAuthRow = (
  row: Omit<UserAuthRow, "id"> & { id: string }
): UserAuthRow => ({
  ...row,
  id: toDomainId(row.id, "users.id"),
});

const loadUserByEmail = async (email: string, db: DbExecutor = getDb()) => {
  const row = await db
    .selectFrom("users")
    .select(["id", "email", "password", "is_admin", "chat_id"])
    .where("email", "=", email)
    .executeTakeFirst();

  return row ? normalizeUserAuthRow(row) : null;
};

const loadUserById = async (userId: number, db: DbExecutor = getDb()) => {
  const row = await db
    .selectFrom("users")
    .select(["id", "email", "password", "is_admin", "chat_id"])
    .where("id", "=", String(userId))
    .executeTakeFirst();

  return row ? normalizeUserAuthRow(row) : null;
};

export const authRepository = {
  loadUserByEmail: (email: string) => loadUserByEmail(email),
  loadUserById: (userId: number) => loadUserById(userId),
};
