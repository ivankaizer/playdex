import {
  createDb,
  ensureDatabaseExists,
  getSqlClient,
  type Db,
  type SqlClient,
} from "../../src/common/db/database";
import { runMigrations } from "../../src/common/db/migrations";

export type TestDatabase = {
  db: Db;
  sql: SqlClient;
  cleanup: () => Promise<void>;
  databaseUrl: string;
  dbName: string;
};

const createTestSqlOptions = () => ({
  max: 1,
  onnotice: (_notice: unknown) => {
    // Keep integration test output clean.
  },
});

const normalizeDbName = (name: string) =>
  name.toLowerCase().replace(/[^a-z0-9_]/g, "_");

const isTestDatabaseName = (name: string) => /(^|_)test($|_)/.test(name);

const parseDotnetConnectionString = (raw: string): URL => {
  const params = new Map<string, string>();
  for (const part of raw.split(";")) {
    const idx = part.indexOf("=");
    if (idx === -1) continue;
    params.set(
      part.slice(0, idx).trim().toLowerCase(),
      part.slice(idx + 1).trim()
    );
  }
  const host = params.get("host") ?? params.get("server") ?? "localhost";
  const port = params.get("port") ?? "5432";
  const database = params.get("database") ?? "postgres";
  const username =
    params.get("username") ?? params.get("user id") ?? "postgres";
  const password = params.get("password") ?? "";
  return new URL(
    `postgres://${encodeURIComponent(username)}:${encodeURIComponent(password)}@${host}:${port}/${database}`
  );
};

const parseConnectionUrl = (raw: string): URL => {
  try {
    return new URL(raw);
  } catch {
    return parseDotnetConnectionString(raw);
  }
};

let sharedSetup: Promise<{ databaseUrl: string; dbName: string }> | null = null;

const ensureTestDatabase = (): Promise<{
  databaseUrl: string;
  dbName: string;
}> => {
  if (sharedSetup) {
    return sharedSetup;
  }

  sharedSetup = (async () => {
    const baseUrl = Bun.env.DATABASE_URL;
    if (!baseUrl) {
      throw new Error("DATABASE_URL is required to run integration tests.");
    }

    const parsed = parseConnectionUrl(baseUrl);
    const baseName = parsed.pathname.replace(/^\//, "") || "postgres";
    const normalizedBase = normalizeDbName(baseName);
    const dbName = isTestDatabaseName(normalizedBase)
      ? normalizedBase
      : `${normalizedBase}_test`;

    const testUrl = new URL(parsed.toString());
    testUrl.pathname = `/${dbName}`;
    const databaseUrl = testUrl.toString();

    Bun.env.DATABASE_URL = databaseUrl;

    await ensureDatabaseExists(databaseUrl);

    const migrationDb = createDb(databaseUrl, createTestSqlOptions());
    try {
      await runMigrations({ db: migrationDb });
    } finally {
      await migrationDb.destroy();
    }

    return { databaseUrl, dbName };
  })();

  return sharedSetup;
};

export const createTestDatabase = async (): Promise<TestDatabase> => {
  const { databaseUrl, dbName } = await ensureTestDatabase();
  const db = createDb(databaseUrl, createTestSqlOptions());
  const sql = getSqlClient(db);

  return {
    db,
    sql,
    cleanup: async () => {
      await db.destroy();
    },
    databaseUrl,
    dbName,
  };
};
