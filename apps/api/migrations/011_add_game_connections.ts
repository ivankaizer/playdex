import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("game_connections")
    .ifNotExists()
    .addColumn("user_id", "bigint", (col) =>
      col.notNull().references("users.id").onDelete("cascade")
    )
    .addColumn("game_id", "bigint", (col) =>
      col.notNull().references("games.id").onDelete("cascade")
    )
    .addColumn("connected_game_id", "bigint", (col) =>
      col.notNull().references("games.id").onDelete("cascade")
    )
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addPrimaryKeyConstraint("game_connections_pkey", [
      "user_id",
      "game_id",
      "connected_game_id",
    ])
    .addCheckConstraint(
      "game_connections_check",
      sql`game_id < connected_game_id`
    )
    .execute();

  await db.schema
    .createIndex("game_connections_user_id_idx")
    .ifNotExists()
    .on("game_connections")
    .column("user_id")
    .execute();
};
