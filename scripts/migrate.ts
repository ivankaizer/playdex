import postgres from "postgres";

import { runMigrations } from "../src/migrations";

const databaseUrl = Bun.env.DATABASE_URL;
if (!databaseUrl?.trim()) {
  console.error("DATABASE_URL is required.");
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
