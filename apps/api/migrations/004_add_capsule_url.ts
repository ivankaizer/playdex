import type { Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .alterTable("games")
    .addColumn("capsule_url", "text", (col) => col.ifNotExists())
    .execute();
};
