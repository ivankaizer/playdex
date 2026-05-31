import { promises as fs } from "node:fs";
import * as path from "node:path";

import { sql } from "kysely";
import { FileMigrationProvider, Migrator } from "kysely/migration";

import {
  createDb,
  getDb,
  withAdvisoryLock,
  type Db,
  type SqlClient,
} from "./database";

type Logger = Pick<Console, "log" | "warn">;

const DEFAULT_MIGRATIONS_DIR = path.join(
  import.meta.dir,
  "../../../migrations"
);
const LEGACY_ADVISORY_LOCK_ID = 1_971_110_001;

const hasTable = async (db: Db, tableName: string) => {
  // Catalog inspection is intentionally isolated to this migration boundary.
  const { rows } = await sql<{ table_name: string | null }>`
    select to_regclass(${`public.${tableName}`})::text as table_name
  `.execute(db);

  return rows[0]?.table_name !== null;
};

const readLegacyHistory = async (db: Db) => {
  const { rows } = await sql<{ version: string }>`
    select version
    from schema_migrations
    order by applied_at, version
  `.execute(db);

  return rows.map((row) => row.version);
};

const readKyselyHistory = async (db: Db) => {
  if (!(await hasTable(db, "kysely_migration"))) {
    return [];
  }

  const { rows } = await sql<{ name: string }>`
    select name
    from kysely_migration
    order by timestamp, name
  `.execute(db);

  return rows.map((row) => row.name);
};

const ensureKyselyMigrationTable = async (db: Db) => {
  await db.schema
    .createTable("kysely_migration")
    .ifNotExists()
    .addColumn("name", "varchar(255)", (col) => col.notNull().primaryKey())
    .addColumn("timestamp", "varchar(255)", (col) => col.notNull())
    .execute();
};

const mapLegacyHistory = (
  legacyVersions: string[],
  migrationNames: string[]
) => {
  const seen = new Set<string>();
  const mapped = legacyVersions.map((version) => {
    if (seen.has(version)) {
      throw new Error(
        `Legacy migration history contains duplicate version: ${version}`
      );
    }
    seen.add(version);

    if (!version.endsWith(".sql")) {
      throw new Error(
        `Legacy migration history contains unknown version: ${version}`
      );
    }

    const migrationName = version.slice(0, -".sql".length);
    if (!migrationNames.includes(migrationName)) {
      throw new Error(
        `Legacy migration history contains unknown version: ${version}`
      );
    }

    return migrationName;
  });

  const expectedPrefix = migrationNames.slice(0, mapped.length);
  if (
    mapped.some(
      (migrationName, index) => migrationName !== expectedPrefix[index]
    )
  ) {
    throw new Error(
      `Legacy migration history must be an ordered prefix. Received: ${legacyVersions.join(
        ", "
      )}`
    );
  }

  return mapped;
};

const seedKyselyHistory = async (db: Db, migrationNames: string[]) => {
  if (migrationNames.length === 0) {
    return;
  }

  await ensureKyselyMigrationTable(db);
  for (const migrationName of migrationNames) {
    await sql`
      insert into kysely_migration (name, timestamp)
      values (${migrationName}, ${new Date().toISOString()})
    `.execute(db);
  }
};

const adoptLegacyHistory = async (db: Db, migrationNames: string[]) => {
  const legacyVersions = await readLegacyHistory(db);
  const legacyHistory = mapLegacyHistory(legacyVersions, migrationNames);
  const kyselyHistory = await readKyselyHistory(db);

  if (kyselyHistory.length === 0) {
    await seedKyselyHistory(db, legacyHistory);
    return;
  }

  const canonicalPrefix = kyselyHistory.slice(0, legacyHistory.length);
  const historiesMatch =
    kyselyHistory.length >= legacyHistory.length &&
    canonicalPrefix.every(
      (migrationName, index) => migrationName === legacyHistory[index]
    );

  if (!historiesMatch) {
    throw new Error(
      `Legacy and Kysely migration histories diverge. Legacy: ${legacyHistory.join(
        ", "
      )}. Kysely: ${kyselyHistory.join(", ")}`
    );
  }
};

const toErrorMessage = (error: unknown) =>
  error instanceof Error ? error.message : String(error);

export const runMigrations = async ({
  db,
  sql,
  migrationsDir = DEFAULT_MIGRATIONS_DIR,
  logger = console,
}: {
  db?: Db;
  sql?: SqlClient;
  migrationsDir?: string;
  logger?: Logger;
} = {}) => {
  const migrationDb =
    db ??
    (sql ? createDb("postgres://compatibility-client", { sql }) : getDb());

  const provider = new FileMigrationProvider({
    fs,
    path,
    migrationFolder: migrationsDir,
  });
  const migrationNames = Object.keys(await provider.getMigrations()).sort();

  const migrateToLatest = async () => {
    const migrator = new Migrator({
      db: migrationDb,
      provider,
    });
    const { error, results } = await migrator.migrateToLatest();

    for (const result of results ?? []) {
      if (result.status === "Success") {
        logger.log(`Applying migration ${result.migrationName}`);
      } else if (result.status === "Error") {
        logger.warn(
          `Failed migration ${result.migrationName} (${result.direction})`
        );
      }
    }

    if (error) {
      const failed = results?.find((result) => result.status === "Error");
      const context = failed
        ? `Migration ${failed.migrationName} (${failed.direction}) failed: `
        : "Migration failed: ";
      throw new Error(`${context}${toErrorMessage(error)}`);
    }

    logger.log("Migrations complete.");
  };

  if (!(await hasTable(migrationDb, "schema_migrations"))) {
    await migrateToLatest();
    return;
  }

  await withAdvisoryLock(migrationDb, LEGACY_ADVISORY_LOCK_ID, async () => {
    await adoptLegacyHistory(migrationDb, migrationNames);
    await migrateToLatest();
  });
};
