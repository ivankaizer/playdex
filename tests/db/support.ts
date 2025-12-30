import postgres, { PendingQuery, Row } from "postgres";

export type DbQuery = {
  text: string;
  values?: unknown[];
};

export type DbTestExpectation = {
  rowCount?: number;
  rows?: Row[];
};

export type DbTestCase = {
  name: string;
  seed: string[];
  query: (sql: ReturnType<typeof postgres>) => PendingQuery<Row[]>;
  expect: DbTestExpectation;
};

export const defineDbTest = (test: DbTestCase) => test;

export const resetDatabase = async (sql: ReturnType<typeof postgres>) => {
  const tables = await sql<{ tablename: string }[]>`
    select tablename
    from pg_tables
    where schemaname = 'public' and tablename <> 'schema_migrations'
  `;

  if (tables.length === 0) {
    return;
  }

  const tableList = tables.map((row) => `"${row.tablename}"`).join(", ");
  await sql.unsafe(`truncate table ${tableList} restart identity cascade`);
};
