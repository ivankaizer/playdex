import postgres from "postgres";

let sql: ReturnType<typeof postgres> | null = null;

const shouldIgnoreNotice = (notice: { code?: string; message?: string }) => {
  return (
    notice.code === "42P07" && notice.message?.includes("schema_migrations")
  );
};

export const getSql = () => {
  if (sql) {
    return sql;
  }

  const databaseUrl = Bun.env.DATABASE_URL;
  if (!databaseUrl?.trim()) {
    throw new Error("DATABASE_URL is required.");
  }

  sql = postgres(databaseUrl, {
    max: 10,
    onnotice: (notice) => {
      if (shouldIgnoreNotice(notice)) {
        return;
      }

      const severity = notice.severity ?? "NOTICE";
      const code = notice.code ? ` ${notice.code}` : "";
      const message = notice.message ?? "Database notice.";
      console.warn(`${severity}${code}: ${message}`);
    },
  });
  return sql;
};
