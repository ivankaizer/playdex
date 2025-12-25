import { beforeAll, afterAll, expect, test } from "bun:test";
import { readdir } from "node:fs/promises";
import { join } from "node:path";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;

let db: TestDatabase | null = null;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }
  db = await createTestDatabase();
});

afterAll(async () => {
  if (db) {
    await db.cleanup();
  }
});

requiresDb("runMigrations applies all SQL files", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const migrationsDir = join(process.cwd(), "migrations");
  const files = (await readdir(migrationsDir)).filter((file) =>
    file.endsWith(".sql")
  );

  const applied = await db.sql<{ version: string }[]>`
    select version from schema_migrations
  `;

  expect(applied.map((row) => row.version).sort()).toEqual(files.sort());
});

requiresDb("runMigrations creates expected tables", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const rows = await db.sql<{ table_name: string }[]>`
    select table_name
    from information_schema.tables
    where table_schema = 'public'
  `;

  const tableNames = rows.map((row) => row.table_name);
  expect(tableNames).toContain("users");
  expect(tableNames).toContain("platform_credentials");
  expect(tableNames).toContain("games");
  expect(tableNames).toContain("play_sessions");
  expect(tableNames).toContain("poll_results");
});
