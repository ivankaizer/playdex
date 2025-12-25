import postgres from "postgres";

import { runMigrations } from "../../src/infrastructure/db/migrations";

export type TestDatabase = {
  sql: ReturnType<typeof postgres>;
  cleanup: () => Promise<void>;
  databaseUrl: string;
  dbName: string;
};

const normalizeDbName = (name: string) =>
  name.toLowerCase().replace(/[^a-z0-9_]/g, "_");

const buildAdminUrl = (baseUrl: URL) => {
  const adminUrl = new URL(baseUrl.toString());
  adminUrl.pathname = "/postgres";
  return adminUrl;
};

export const createTestDatabase = async (): Promise<TestDatabase> => {
  const baseUrl = Bun.env.DATABASE_URL;
  if (!baseUrl) {
    throw new Error("DATABASE_URL is required to run integration tests.");
  }

  const parsed = new URL(baseUrl);
  const baseName = parsed.pathname.replace(/^\//, "") || "postgres";
  const dbName = normalizeDbName(`${baseName}_test`);

  const adminUrl = buildAdminUrl(parsed);
  const adminSql = postgres(adminUrl.toString(), { max: 1 });
  await adminSql.unsafe(`create database "${dbName}"`);
  await adminSql.end({ timeout: 5 });

  const testUrl = new URL(baseUrl);
  testUrl.pathname = `/${dbName}`;
  const sql = postgres(testUrl.toString(), { max: 1 });
  await runMigrations({ sql });

  const cleanup = async () => {
    await sql.end({ timeout: 5 });
    const admin = postgres(adminUrl.toString(), { max: 1 });
    try {
      await admin.unsafe(`drop database if exists "${dbName}" with (force)`);
    } catch {
      await admin.unsafe(`drop database if exists "${dbName}"`);
    }
    await admin.end({ timeout: 5 });
  };

  return {
    sql,
    cleanup,
    databaseUrl: testUrl.toString(),
    dbName,
  };
};
