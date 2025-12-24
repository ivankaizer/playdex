import postgres from "postgres";

let sql: ReturnType<typeof postgres> | null = null;

export const getSql = () => {
  if (sql) {
    return sql;
  }

  const databaseUrl = Bun.env.DATABASE_URL;
  if (!databaseUrl?.trim()) {
    throw new Error("DATABASE_URL is required.");
  }

  sql = postgres(databaseUrl, { max: 10 });
  return sql;
};
