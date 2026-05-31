import { expect, test } from "bun:test";
import { readFile } from "node:fs/promises";
import * as path from "node:path";

const readApiFile = (relativePath: string) =>
  readFile(path.join(import.meta.dir, "..", relativePath), "utf8");

test("startup and CLI use the shared Kysely migration runner", async () => {
  const [startup, migrateScript] = await Promise.all([
    readApiFile("src/index.ts"),
    readApiFile("scripts/migrate.ts"),
  ]);

  expect(startup).toContain(
    'import { runMigrations } from "./common/db/migrations"'
  );
  expect(startup).toContain("await runMigrations();");

  expect(migrateScript).toContain("createDb");
  expect(migrateScript).toContain("ensureDatabaseExists");
  expect(migrateScript).toContain("await runMigrations({ db });");
  expect(migrateScript).toContain("await db.destroy();");
  expect(migrateScript).not.toContain('from "postgres"');
});
