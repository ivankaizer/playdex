import postgres from "postgres";

import { runMigrations } from "../../src/common/db/migrations";

export type TestDatabase = {
  sql: ReturnType<typeof postgres>;
  cleanup: () => Promise<void>;
  databaseUrl: string;
  dbName: string;
};

const testSqlOptions = {
  max: 1,
  onnotice: (_notice: unknown) => {
    // Keep integration test output clean.
  },
};

const normalizeDbName = (name: string) =>
  name.toLowerCase().replace(/[^a-z0-9_]/g, "_");

const buildAdminUrl = (baseUrl: URL) => {
  const adminUrl = new URL(baseUrl.toString());
  adminUrl.pathname = "/postgres";
  return adminUrl;
};

const isTestDatabaseName = (name: string) => /(^|_)test($|_)/.test(name);

const parseDotnetConnectionString = (raw: string): URL => {
  const params = new Map<string, string>();
  for (const part of raw.split(";")) {
    const idx = part.indexOf("=");
    if (idx === -1) continue;
    params.set(part.slice(0, idx).trim().toLowerCase(), part.slice(idx + 1).trim());
  }
  const host = params.get("host") ?? params.get("server") ?? "localhost";
  const port = params.get("port") ?? "5432";
  const database = params.get("database") ?? "postgres";
  const username = params.get("username") ?? params.get("user id") ?? "postgres";
  const password = params.get("password") ?? "";
  return new URL(`postgres://${encodeURIComponent(username)}:${encodeURIComponent(password)}@${host}:${port}/${database}`);
};

const parseConnectionUrl = (raw: string): URL => {
  try {
    return new URL(raw);
  } catch {
    return parseDotnetConnectionString(raw);
  }
};

let sharedSetup: Promise<{ databaseUrl: string; dbName: string }> | null = null;

const ensureTestDatabase = (): Promise<{ databaseUrl: string; dbName: string }> => {
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

    const adminUrl = buildAdminUrl(parsed);
    const adminSql = postgres(adminUrl.toString(), testSqlOptions);
    const existing = await adminSql<{ exists: boolean }[]>`
      select exists(select 1 from pg_database where datname = ${dbName})
    `;
    if (!existing[0]?.exists) {
      await adminSql.unsafe(`create database "${dbName}"`);
    }
    await adminSql.end({ timeout: 5 });

    const testUrl = new URL(parsed.toString());
    testUrl.pathname = `/${dbName}`;
    const databaseUrl = testUrl.toString();

    Bun.env.DATABASE_URL = databaseUrl;

    const migrationSql = postgres(databaseUrl, testSqlOptions);
    await runMigrations({ sql: migrationSql });
    await migrationSql.end({ timeout: 5 });

    return { databaseUrl, dbName };
  })();

  return sharedSetup;
};

export const createTestDatabase = async (): Promise<TestDatabase> => {
  const { databaseUrl, dbName } = await ensureTestDatabase();
  const sql = postgres(databaseUrl, testSqlOptions);

  return {
    sql,
    cleanup: async () => {
      await sql.end({ timeout: 5 });
    },
    databaseUrl,
    dbName,
  };
};
