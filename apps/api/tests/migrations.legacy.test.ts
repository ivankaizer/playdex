import { expect, test } from "bun:test";
import { promises as fs } from "node:fs";
import * as path from "node:path";

import type { Kysely } from "kysely";
import { FileMigrationProvider } from "kysely/migration";
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
    await callback({ db, sql });
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

const getMigrationNames = async () => {
  const provider = new FileMigrationProvider({
    fs,
    path,
    migrationFolder: migrationsDir,
  });

  return Object.keys(await provider.getMigrations()).sort();
};

const applyHistoricalPrefix = async (db: Db, count: number) => {
  const provider = new FileMigrationProvider({
    fs,
    path,
    migrationFolder: migrationsDir,
  });
  const migrations = await provider.getMigrations();

  for (const migrationName of Object.keys(migrations).sort().slice(0, count)) {
    await migrations[migrationName].up(db as Kysely<unknown>);
  }
};

const seedLegacyHistory = async (sql: SqlClient, names: string[]) => {
  await sql`
    create table schema_migrations (
      version text not null,
      applied_at timestamptz not null default now()
    )
  `;

  for (const name of names) {
    await sql`insert into schema_migrations (version) values (${`${name}.sql`})`;
  }
};

const createLogger = () => {
  const logs: string[] = [];
  return {
    logger: {
      log: (message: string) => logs.push(message),
      warn: (message: string) => logs.push(message),
    },
    logs,
  };
};

requiresDb(
  "runMigrations adopts fully applied legacy history without replaying modules",
  async () => {
    await withDisposableDatabase("legacy_full", async ({ db, sql }) => {
      const migrationNames = await getMigrationNames();
      await applyHistoricalPrefix(db, migrationNames.length);
      await seedLegacyHistory(sql, migrationNames);
      const { logger, logs } = createLogger();

      await runMigrations({ db, logger });

      const canonical = await sql<{ name: string }[]>`
        select name from kysely_migration order by name
      `;
      expect(canonical.map((row) => row.name)).toEqual(migrationNames);
      expect(
        logs.filter((line) => line.startsWith("Applying migration "))
      ).toEqual([]);
    });
  }
);

requiresDb(
  "runMigrations adopts a legacy prefix and applies only pending modules",
  async () => {
    await withDisposableDatabase("legacy_prefix", async ({ db, sql }) => {
      const migrationNames = await getMigrationNames();
      const prefix = migrationNames.slice(0, 6);
      await applyHistoricalPrefix(db, prefix.length);
      await seedLegacyHistory(sql, prefix);
      const { logger, logs } = createLogger();

      await runMigrations({ db, logger });

      const canonical = await sql<{ name: string }[]>`
        select name from kysely_migration order by name
      `;
      expect(canonical.map((row) => row.name)).toEqual(migrationNames);
      expect(
        logs
          .filter((line) => line.startsWith("Applying migration "))
          .map((line) => line.replace("Applying migration ", ""))
      ).toEqual(migrationNames.slice(prefix.length));
    });
  }
);

requiresDb("runMigrations rejects unknown legacy versions", async () => {
  await withDisposableDatabase("legacy_unknown", async ({ db, sql }) => {
    await seedLegacyHistory(sql, ["999_unknown"]);

    expect(runMigrations({ db })).rejects.toThrow(
      "Legacy migration history contains unknown version"
    );
    expect(await sql`select to_regclass('public.users') as users`).toEqual([
      { users: null },
    ]);
  });
});

requiresDb("runMigrations rejects duplicate legacy versions", async () => {
  await withDisposableDatabase("legacy_duplicate", async ({ db, sql }) => {
    await seedLegacyHistory(sql, ["001_init", "001_init"]);

    expect(runMigrations({ db })).rejects.toThrow(
      "Legacy migration history contains duplicate version"
    );
    expect(await sql`select to_regclass('public.users') as users`).toEqual([
      { users: null },
    ]);
  });
});

requiresDb(
  "runMigrations rejects legacy history with missing predecessors",
  async () => {
    await withDisposableDatabase("legacy_missing", async ({ db, sql }) => {
      await seedLegacyHistory(sql, ["002_poll_results"]);

      expect(runMigrations({ db })).rejects.toThrow(
        "Legacy migration history must be an ordered prefix"
      );
      expect(await sql`select to_regclass('public.users') as users`).toEqual([
        { users: null },
      ]);
    });
  }
);

requiresDb(
  "runMigrations rejects divergent legacy and Kysely histories",
  async () => {
    await withDisposableDatabase("legacy_divergent", async ({ db, sql }) => {
      const migrationNames = await getMigrationNames();
      await runMigrations({ db });
      await sql`delete from kysely_migration where name = ${migrationNames.at(-1)}`;
      await seedLegacyHistory(sql, migrationNames);

      expect(runMigrations({ db })).rejects.toThrow(
        "Legacy and Kysely migration histories diverge"
      );
    });
  }
);
