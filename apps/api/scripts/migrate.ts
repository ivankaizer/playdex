import postgres from "postgres";

import { runMigrations } from "../src/common/db/migrations";

const databaseUrl = Bun.env.DATABASE_URL;
if (!databaseUrl?.trim()) {
  console.error("DATABASE_URL is required.");
  process.exit(1);
}

const ensureDatabaseExists = async (url: string) => {
  const parsed = new URL(url);
  const databaseName = decodeURIComponent(parsed.pathname.replace(/^\//, ""));
  if (!databaseName) {
    throw new Error("DATABASE_URL must include a database name.");
  }

  const adminUrl = new URL(url);
  adminUrl.pathname = "/postgres";

  const adminSql = postgres(adminUrl.toString(), {
    max: 1,
  });

  try {
    const rows = await adminSql<{ exists: boolean }[]>`
      select exists (
        select 1
        from pg_database
        where datname = ${databaseName}
      ) as "exists"
    `;

    if (!rows[0]?.exists) {
      const quoted = `"${databaseName.replace(/"/g, '""')}"`;
      await adminSql.unsafe(`create database ${quoted}`);
    }
  } finally {
    await adminSql.end();
  }
};

try {
  await ensureDatabaseExists(databaseUrl);
} catch (error) {
  console.error("Database setup failed.");
  console.error(error);
  process.exit(1);
}

const sql = postgres(databaseUrl, {
  max: 1,
  onnotice: (notice) => {
    if (notice.code === "42P07") {
      return;
    }
    console.warn(notice);
  },
});

try {
  await runMigrations({ sql });
} catch (error) {
  console.error("Migration failed.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await sql.end();
}
