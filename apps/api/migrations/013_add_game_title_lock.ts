import type { Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .alterTable("games")
    .addColumn("title_locked", "boolean", (col) =>
      col.ifNotExists().notNull().defaultTo(false)
    )
    .execute();
};
