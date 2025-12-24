import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";

import postgres from "postgres";

const databaseUrl = Bun.env.DATABASE_URL;
if (!databaseUrl?.trim()) {
  console.error("DATABASE_URL is required.");
  process.exit(1);
}

const migrationsDir = join(process.cwd(), "migrations");
const sql = postgres(databaseUrl, {
  max: 1,
  onnotice: (notice) => {
    if (notice.code === "42P07") {
      return;
    }
    console.warn(notice);
  },
});

const ensureMigrationsTable = async () => {
  await sql`
    create table if not exists schema_migrations (
      version text primary key,
      applied_at timestamptz not null default now()
    )
  `;
};

const loadMigrations = async () => {
  const entries = await readdir(migrationsDir, { withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.endsWith(".sql"))
    .map((entry) => entry.name)
    .sort();
};

const runMigrations = async () => {
  await ensureMigrationsTable();

  const appliedRows = await sql<{ version: string }[]>`
    select version from schema_migrations
  `;
  const applied = new Set(appliedRows.map((row) => row.version));
  const migrations = await loadMigrations();

  for (const filename of migrations) {
    if (applied.has(filename)) {
      continue;
    }

    const contents = await readFile(join(migrationsDir, filename), "utf8");
    console.log(`Applying migration ${filename}`);

    await sql.begin(async (tx) => {
      await tx.unsafe(contents);
      await tx`insert into schema_migrations (version) values (${filename})`;
    });
  }

  console.log("Migrations complete.");
};

try {
  await runMigrations();
} catch (error) {
  console.error("Migration failed.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await sql.end();
}
