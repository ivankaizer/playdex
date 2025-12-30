import { afterAll, beforeAll, expect, test } from "bun:test";

import type { TestDatabase } from "../helpers/db";
import { createTestDatabase } from "../helpers/db";
import { dbTests } from "./cases";
import { resetDatabase } from "./support";

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

for (const testCase of dbTests) {
  requiresDb(testCase.name, async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    await resetDatabase(db.sql);
    for (const statement of testCase.seed) {
      await db.sql.unsafe(statement);
    }

    const rows = await testCase.query(db.sql);

    if (testCase.expect.rowCount !== undefined) {
      expect(rows.length).toBe(testCase.expect.rowCount);
    }

    if (testCase.expect.rows !== undefined) {
      // @ts-ignore
      expect(rows).toEqual(testCase.expect.rows);
    }
  });
}
