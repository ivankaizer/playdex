import type { Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .alterTable("platform_credentials")
    .addColumn("active", "boolean", (col) =>
      col.ifNotExists().notNull().defaultTo(true)
    )
    .execute();
};
