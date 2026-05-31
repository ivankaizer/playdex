import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("ignored_games")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("user_id", "bigint", (col) =>
      col.notNull().references("users.id").onDelete("cascade")
    )
    .addColumn("game_id", "bigint", (col) =>
      col.notNull().references("games.id").onDelete("cascade")
    )
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addUniqueConstraint("ignored_games_user_id_game_id_key", [
      "user_id",
      "game_id",
    ])
    .execute();

  await db.schema
    .createIndex("ignored_games_user_id_idx")
    .ifNotExists()
    .on("ignored_games")
    .column("user_id")
    .execute();
};
