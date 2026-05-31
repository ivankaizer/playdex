import { Kysely, type Transaction } from "kysely";
import { PostgresJSDialect } from "kysely-postgres-js";
import postgres from "postgres";

import type { Database } from "./schema";

export type Db = Kysely<Database>;
export type DbExecutor = Db | Transaction<Database>;
export type SqlClient = ReturnType<typeof postgres>;

type PostgresOptions = NonNullable<Parameters<typeof postgres>[1]>;
type CreateSql = (databaseUrl: string, options?: PostgresOptions) => SqlClient;

export type CreateDbOptions = {
  max?: number;
  onnotice?: (notice: postgres.Notice) => void;
  sql?: SqlClient;
};

export type EnsureDatabaseExistsOptions = {
  createSql?: CreateSql;
};

type DatabaseResources = {
  db: Db;
  destroy: () => Promise<void>;
  sql: SqlClient;
};

const SUPPORTED_DATABASE_NAME = /^[a-zA-Z0-9_-]+$/;
const resourcesByDb = new WeakMap<Db, DatabaseResources>();

let singleton: DatabaseResources | null = null;
let singletonDatabaseUrl: string | null = null;

const createSql: CreateSql = (databaseUrl, options) =>
  postgres(databaseUrl, options);

const shouldIgnoreNotice = (notice: postgres.Notice) =>
  notice.code === "42P07" && notice.message?.includes("schema_migrations");

const logNotice = (notice: postgres.Notice) => {
  if (shouldIgnoreNotice(notice)) {
    return;
  }

  const severity = notice.severity ?? "NOTICE";
  const code = notice.code ? ` ${notice.code}` : "";
  const message = notice.message ?? "Database notice.";
  console.warn(`${severity}${code}: ${message}`);
};

const requireDatabaseUrl = () => {
  const databaseUrl = Bun.env.DATABASE_URL;
  if (!databaseUrl?.trim()) {
    throw new Error("DATABASE_URL is required.");
  }

  return databaseUrl;
};

const createDatabaseResources = (
  databaseUrl: string,
  { max = 10, onnotice = logNotice, sql: providedSql }: CreateDbOptions = {}
): DatabaseResources => {
  const sql = providedSql ?? createSql(databaseUrl, { max, onnotice });
  let dialectInitialized = false;

  const db = new Kysely<Database>({
    dialect: new PostgresJSDialect({
      postgres: async () => {
        dialectInitialized = true;
        return sql;
      },
    }),
  });

  const resources = {
    db,
    sql,
    destroy: async () => {
      if (dialectInitialized) {
        await db.destroy();
        return;
      }

      await sql.end({ timeout: 5 });
    },
  };

  resourcesByDb.set(db, resources);
  return resources;
};

export const quoteDatabaseName = (databaseName: string) => {
  if (!SUPPORTED_DATABASE_NAME.test(databaseName)) {
    throw new Error(
      "Database name may contain only letters, numbers, underscores, and hyphens."
    );
  }

  return `"${databaseName}"`;
};

export const ensureDatabaseExists = async (
  databaseUrl: string,
  { createSql: createAdminSql = createSql }: EnsureDatabaseExistsOptions = {}
) => {
  const parsed = new URL(databaseUrl);
  const databaseName = decodeURIComponent(parsed.pathname.replace(/^\//, ""));
  const quotedDatabaseName = quoteDatabaseName(databaseName);

  const adminUrl = new URL(databaseUrl);
  adminUrl.pathname = "/postgres";

  const sql = createAdminSql(adminUrl.toString(), { max: 1 });

  try {
    const rows = await sql<{ exists: boolean }[]>`
      select exists (
        select 1
        from pg_database
        where datname = ${databaseName}
      ) as "exists"
    `;

    if (!rows[0]?.exists) {
      // PostgreSQL does not allow parameter placeholders for identifiers.
      await sql.unsafe(`create database ${quotedDatabaseName}`);
    }
  } finally {
    await sql.end({ timeout: 5 });
  }
};

export const createDb = (databaseUrl: string, options?: CreateDbOptions): Db =>
  createDatabaseResources(databaseUrl, options).db;

export const getDb = () => {
  const databaseUrl = requireDatabaseUrl();
  if (singleton && singletonDatabaseUrl === databaseUrl) {
    return singleton.db;
  }

  if (singleton) {
    singleton.destroy().catch(() => undefined);
  }

  singleton = createDatabaseResources(databaseUrl);
  singletonDatabaseUrl = databaseUrl;
  return singleton.db;
};

export const getSqlClient = (db = getDb()) => {
  const resources = resourcesByDb.get(db);
  if (!resources) {
    throw new Error("Database was not created by the shared database factory.");
  }

  return resources.sql;
};

export const withAdvisoryLock = async <Result>(
  db: Db,
  lockId: number,
  callback: () => Promise<Result>
) => {
  const sql = getSqlClient(db);
  const reservedSql = await sql.reserve();

  try {
    // Session-level advisory locks need a reserved driver connection.
    await reservedSql`select pg_advisory_lock(${lockId})`;
    return await callback();
  } finally {
    await reservedSql`select pg_advisory_unlock(${lockId})`;
    reservedSql.release();
  }
};

export const destroyDb = async () => {
  const resources = singleton;
  singleton = null;
  singletonDatabaseUrl = null;

  if (resources) {
    await resources.destroy();
  }
};
