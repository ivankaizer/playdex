import { readdir, readFile } from "node:fs/promises";
import { join } from "node:path";

import type postgres from "postgres";

import { getSql } from "./postgres";

type Logger = Pick<Console, "log" | "warn">;
type SqlClient = ReturnType<typeof postgres>;

const DEFAULT_MIGRATIONS_DIR = join(process.cwd(), "migrations");

const ensureMigrationsTable = async (sql: SqlClient) => sql`
  create table if not exists schema_migrations (
    version text primary key,
    applied_at timestamptz not null default now()
  )
`;

const fetchAppliedMigrations = async (sql: SqlClient) =>
  sql<{ version: string }[]>`
    select version from schema_migrations
  `;

const insertAppliedMigration = async (filename: string, sql: SqlClient) => sql`
  insert into schema_migrations (version) values (${filename})
`;

const loadMigrations = async (migrationsDir: string) => {
  const entries = await readdir(migrationsDir, { withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.endsWith(".sql"))
    .map((entry) => entry.name)
    .sort();
};

export const runMigrations = async ({
  sql = getSql(),
  migrationsDir = DEFAULT_MIGRATIONS_DIR,
  logger = console,
}: {
  sql?: SqlClient;
  migrationsDir?: string;
  logger?: Logger;
} = {}) => {
  await ensureMigrationsTable(sql);

  const appliedRows = await fetchAppliedMigrations(sql);
  const applied = new Set(appliedRows.map((row) => row.version));
  const migrations = await loadMigrations(migrationsDir);

  for (const filename of migrations) {
    if (applied.has(filename)) {
      continue;
    }

    const contents = await readFile(join(migrationsDir, filename), "utf8");
    logger.log(`Applying migration ${filename}`);

    await sql.begin(async (tx) => {
      await tx.unsafe(contents);
      await insertAppliedMigration(filename, tx);
    });
  }

  logger.log("Migrations complete.");
};
