import { expect, test } from "bun:test";
import { copyFile, mkdtemp, readdir, rm, writeFile } from "node:fs/promises";
import * as path from "node:path";

import postgres from "postgres";

import {
  createDb,
  quoteDatabaseName,
  type Db,
  type SqlClient,
} from "../src/common/db/database";
import { runMigrations } from "../src/common/db/migrations";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;
const migrationsDir = path.join(import.meta.dir, "../migrations");

type TestDatabase = {
  databaseUrl: string;
  db: Db;
  sql: SqlClient;
};

const createSqlOptions = () => ({
  max: 4,
  onnotice: (_notice: unknown) => {
    // Keep integration test output clean.
  },
});

const withDisposableDatabase = async (
  label: string,
  callback: (database: TestDatabase) => Promise<void>
) => {
  const baseUrl = Bun.env.DATABASE_URL;
  if (!baseUrl) {
    throw new Error("DATABASE_URL is required.");
  }

  const databaseName = `playdex_kysely_${label}_${Date.now()}_${Math.random()
    .toString(36)
    .slice(2)}`;
  const adminUrl = new URL(baseUrl);
  adminUrl.pathname = "/postgres";
  const adminSql = postgres(adminUrl.toString(), createSqlOptions());
  await adminSql.unsafe(`create database ${quoteDatabaseName(databaseName)}`);

  const databaseUrl = new URL(baseUrl);
  databaseUrl.pathname = `/${databaseName}`;
  const sql = postgres(databaseUrl.toString(), createSqlOptions());
  const db = createDb(databaseUrl.toString(), { sql });

  try {
    await callback({ databaseUrl: databaseUrl.toString(), db, sql });
  } finally {
    await db.destroy();
    await adminSql`
      select pg_terminate_backend(pid)
      from pg_stat_activity
      where datname = ${databaseName}
        and pid <> pg_backend_pid()
    `;
    await adminSql.unsafe(`drop database ${quoteDatabaseName(databaseName)}`);
    await adminSql.end({ timeout: 5 });
  }
};

const withFailingMigrationsDir = async (
  callback: (directory: string) => Promise<void>
) => {
  const directory = await mkdtemp(
    path.join(import.meta.dir, "../.tmp-playdex-migrations-")
  );

  try {
    for (const filename of await readdir(migrationsDir)) {
      await copyFile(
        path.join(migrationsDir, filename),
        path.join(directory, filename)
      );
    }

    await writeFile(
      path.join(directory, "014_expected_failure.ts"),
      `export const up = async (db) => {
  await db.schema
    .createTable("should_rollback")
    .addColumn("id", "integer")
    .execute();
  throw new Error("expected migration failure");
};
`
    );

    await callback(directory);
  } finally {
    await rm(directory, { force: true, recursive: true });
  }
};

requiresDb(
  "runMigrations rolls back a failing TypeScript migration",
  async () => {
    await withDisposableDatabase("rollback", async ({ db, sql }) => {
      await withFailingMigrationsDir(async (directory) => {
        expect(runMigrations({ db, migrationsDir: directory })).rejects.toThrow(
          "Migration 014_expected_failure (Up) failed: expected migration failure"
        );

        expect(
          await sql`select to_regclass('public.should_rollback') as table_name`
        ).toEqual([{ table_name: null }]);
        expect(
          await sql<{ name: string }[]>`
          select name from kysely_migration where name = '014_expected_failure'
        `
        ).toEqual([]);
      });
    });
  }
);

requiresDb(
  "runMigrations serializes concurrent runners and records each migration once",
  async () => {
    await withDisposableDatabase(
      "concurrent",
      async ({ databaseUrl, db, sql }) => {
        const secondDb = createDb(databaseUrl, createSqlOptions());

        try {
          await Promise.all([
            runMigrations({ db }),
            runMigrations({ db: secondDb }),
          ]);
        } finally {
          await secondDb.destroy();
        }

        const canonical = await sql<{ name: string; count: string }[]>`
          select name, count(*)::text as count
          from kysely_migration
          group by name
          order by name
        `;
        expect(canonical).toHaveLength(13);
        expect(canonical.every((migration) => migration.count === "1")).toBe(
          true
        );
      }
    );
  }
);
