import { afterAll, beforeAll, expect, test } from "bun:test";
import { readdir } from "node:fs/promises";
import { join } from "node:path";

import postgres from "postgres";

import { quoteDatabaseName } from "../src/common/db/database";
import { runMigrations } from "../src/common/db/migrations";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;

let adminSql: ReturnType<typeof postgres> | null = null;
let sql: ReturnType<typeof postgres> | null = null;
let databaseName: string | null = null;

const createSqlOptions = () => ({
  max: 1,
  onnotice: (_notice: unknown) => {
    // Keep integration test output clean.
  },
});

beforeAll(async () => {
  const baseUrl = Bun.env.DATABASE_URL;
  if (!baseUrl) {
    return;
  }

  databaseName = `playdex_kysely_clean_test_${Date.now()}`;
  const quotedDatabaseName = quoteDatabaseName(databaseName);
  const adminUrl = new URL(baseUrl);
  adminUrl.pathname = "/postgres";

  adminSql = postgres(adminUrl.toString(), createSqlOptions());
  await adminSql.unsafe(`create database ${quotedDatabaseName}`);

  const databaseUrl = new URL(baseUrl);
  databaseUrl.pathname = `/${databaseName}`;
  sql = postgres(databaseUrl.toString(), createSqlOptions());

  await runMigrations({ sql });
});

afterAll(async () => {
  if (sql) {
    await sql.end({ timeout: 5 });
  }

  if (adminSql && databaseName) {
    await adminSql`
      select pg_terminate_backend(pid)
      from pg_stat_activity
      where datname = ${databaseName}
        and pid <> pg_backend_pid()
    `;
    await adminSql.unsafe(`drop database ${quoteDatabaseName(databaseName)}`);
    await adminSql.end({ timeout: 5 });
  }
});

requiresDb("runMigrations applies all Kysely modules in order", async () => {
  if (!sql) {
    throw new Error("Test database not initialized.");
  }

  const migrationsDir = join(process.cwd(), "migrations");
  const modules = (await readdir(migrationsDir))
    .filter((file) => file.endsWith(".ts"))
    .map((file) => file.replace(/\.ts$/, ""))
    .sort();

  const applied = await sql<{ name: string }[]>`
    select name from kysely_migration order by name
  `;

  expect(applied.map((row) => row.name)).toEqual(modules);
});

requiresDb("runMigrations creates expected tables", async () => {
  if (!sql) {
    throw new Error("Test database not initialized.");
  }

  const rows = await sql<{ table_name: string }[]>`
    select table_name
    from information_schema.tables
    where table_schema = 'public'
  `;

  const tableNames = rows.map((row) => row.table_name);
  expect(tableNames).toContain("users");
  expect(tableNames).toContain("platforms");
  expect(tableNames).toContain("platform_credentials");
  expect(tableNames).toContain("games");
  expect(tableNames).toContain("play_sessions");
  expect(tableNames).toContain("poll_results");
  expect(tableNames).toContain("manual_game_inputs");
  expect(tableNames).toContain("ignored_games");
  expect(tableNames).toContain("game_ratings");
  expect(tableNames).toContain("user_settings");
  expect(tableNames).toContain("game_connections");
  expect(tableNames).toContain("kysely_migration");
  expect(tableNames).toContain("kysely_migration_lock");
});

requiresDb("runMigrations does not reapply applied migrations", async () => {
  if (!sql) {
    throw new Error("Test database not initialized.");
  }

  const logs: string[] = [];
  const logger = {
    log: (message: string) => logs.push(message),
    warn: (message: string) => logs.push(message),
  };

  await runMigrations({ sql, logger });

  const appliedAgain = logs.filter((line) =>
    line.startsWith("Applying migration ")
  );

  expect(appliedAgain).toEqual([]);
});
