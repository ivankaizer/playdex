import { createDb, ensureDatabaseExists } from "../src/common/db/database";
import { runMigrations } from "../src/common/db/migrations";

const databaseUrl = Bun.env.DATABASE_URL;
if (!databaseUrl?.trim()) {
  console.error("DATABASE_URL is required.");
  process.exit(1);
}

try {
  await ensureDatabaseExists(databaseUrl);
} catch (error) {
  console.error("Database setup failed.");
  console.error(error);
  process.exit(1);
}

const db = createDb(databaseUrl, { max: 2 });

try {
  await runMigrations({ db });
} catch (error) {
  console.error("Migration failed.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await db.destroy();
}
