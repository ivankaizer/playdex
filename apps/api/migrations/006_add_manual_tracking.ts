import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("manual_game_inputs")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("user_id", "bigint", (col) =>
      col.notNull().references("users.id").onDelete("cascade")
    )
    .addColumn("raw_title", "text", (col) => col.notNull())
    .addColumn("canonical_title", "text", (col) => col.notNull())
    .addColumn("mapped_game_id", "bigint", (col) =>
      col.references("games.id").onDelete("set null")
    )
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();

  await db.schema
    .createIndex("manual_game_inputs_user_title_idx")
    .ifNotExists()
    .unique()
    .on("manual_game_inputs")
    .columns(["user_id", "canonical_title"])
    .execute();

  await db.schema
    .createIndex("manual_game_inputs_mapped_game_id_idx")
    .ifNotExists()
    .on("manual_game_inputs")
    .column("mapped_game_id")
    .execute();
};
