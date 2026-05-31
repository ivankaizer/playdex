import type { Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .alterTable("users")
    .addColumn("is_admin", "boolean", (col) =>
      col.ifNotExists().notNull().defaultTo(false)
    )
    .execute();

  await db.schema
    .alterTable("games")
    .addColumn("capsule_url_locked", "boolean", (col) =>
      col.ifNotExists().notNull().defaultTo(false)
    )
    .execute();
};
