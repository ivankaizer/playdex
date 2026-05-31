import type { Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .alterTable("users")
    .addColumn("chat_id", "text", (col) => col.ifNotExists())
    .execute();

  await db.schema
    .createIndex("users_chat_id_unique")
    .ifNotExists()
    .unique()
    .on("users")
    .column("chat_id")
    .where("chat_id", "is not", null)
    .execute();
};
