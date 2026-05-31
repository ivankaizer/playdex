import { expect, test } from "bun:test";
import { sql as kyselySql } from "kysely";

import {
  createDb,
  ensureDatabaseExists,
  quoteDatabaseName,
} from "../src/common/db/database";

type FakeSql = {
  <T extends unknown[]>(
    strings: TemplateStringsArray,
    ...values: unknown[]
  ): Promise<T>;
  end: () => Promise<void>;
  reserve: () => Promise<{
    release: () => void;
    unsafe: () => Promise<unknown[]>;
  }>;
  unsafe: (query: string) => Promise<void>;
};

const createFakeSql = ({
  exists = true,
  queryError,
}: {
  exists?: boolean;
  queryError?: Error;
} = {}) => {
  const unsafeQueries: string[] = [];
  let endCalls = 0;

  const sql = (async () => {
    if (queryError) {
      throw queryError;
    }

    return [{ exists }];
  }) as FakeSql;

  sql.end = async () => {
    endCalls += 1;
  };
  sql.reserve = async () => {
    return {
      release: () => undefined,
      unsafe: async () =>
        Object.assign([], {
          command: "SELECT",
          count: 0,
        }),
    };
  };
  sql.unsafe = async (query: string) => {
    unsafeQueries.push(query);
  };

  return {
    sql,
    unsafeQueries,
    getEndCalls: () => endCalls,
  };
};

test("quotes supported database names", () => {
  expect(quoteDatabaseName("playdex")).toBe('"playdex"');
  expect(quoteDatabaseName("playdex_test-1")).toBe('"playdex_test-1"');
});

test("rejects database names outside the supported identifier subset", () => {
  for (const name of ["", "playdex/test", 'playdex"test', "playdex test"]) {
    expect(() => quoteDatabaseName(name)).toThrow(
      "Database name may contain only letters, numbers, underscores, and hyphens."
    );
  }
});

test("closes the admin client when database setup fails", async () => {
  const fake = createFakeSql({ queryError: new Error("query failed") });

  await expect(
    ensureDatabaseExists("postgres://localhost/playdex", {
      createSql: () => fake.sql,
    })
  ).rejects.toThrow("query failed");

  expect(fake.getEndCalls()).toBe(1);
});

test("creates a missing database with a validated quoted name", async () => {
  const fake = createFakeSql({ exists: false });

  await ensureDatabaseExists("postgres://localhost/playdex-test", {
    createSql: () => fake.sql,
  });

  expect(fake.unsafeQueries).toEqual(['create database "playdex-test"']);
  expect(fake.getEndCalls()).toBe(1);
});

test("destroying a Kysely database closes its Postgres.js client", async () => {
  const fake = createFakeSql();
  const db = createDb("postgres://localhost/playdex", { sql: fake.sql });

  await kyselySql`select 1`.execute(db);
  await db.destroy();

  expect(fake.getEndCalls()).toBe(1);
});
