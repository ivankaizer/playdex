import type postgres from "postgres";

import { getSql } from "../../common/db/postgres";

type Sql = ReturnType<typeof postgres>;

type UserAuthRow = {
  id: number;
  email: string;
  password: string | null;
  chat_id?: string | null;
};

const loadUserByEmail = async (email: string, sql: Sql = getSql()) => {
  const rows = await sql<UserAuthRow[]>`
    select id, email, password, chat_id
    from users
    where email = ${email}
    limit 1
  `;
  return rows[0] ?? null;
};

export const authRepository = {
  loadUserByEmail: (email: string) => loadUserByEmail(email),
};
