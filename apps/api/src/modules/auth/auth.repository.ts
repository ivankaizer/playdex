import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

type Sql = ReturnType<typeof postgres>;

type UserAuthRow = {
  id: number;
  email: string;
  password: string | null;
  is_admin: boolean;
  chat_id?: string | null;
};

const loadUserByEmail = async (email: string, sql: Sql = getSql()) => {
  const rows = await sql<UserAuthRow[]>`
    select id, email, password, is_admin, chat_id
    from users
    where email = ${email}
    limit 1
  `;
  return rows[0] ?? null;
};

const loadUserById = async (userId: number, sql: Sql = getSql()) => {
  const rows = await sql<UserAuthRow[]>`
    select id, email, password, is_admin, chat_id
    from users
    where id = ${userId}
    limit 1
  `;
  return rows[0] ?? null;
};

export const authRepository = {
  loadUserByEmail: (email: string) => loadUserByEmail(email),
  loadUserById: (userId: number) => loadUserById(userId),
};
