import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("game_ratings")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("user_id", "bigint", (col) =>
      col.notNull().references("users.id").onDelete("cascade")
    )
    .addColumn("game_id", "bigint", (col) =>
      col.notNull().references("games.id").onDelete("cascade")
    )
    .addColumn("rating", "smallint", (col) => col.notNull())
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addUniqueConstraint("game_ratings_user_id_game_id_key", [
      "user_id",
      "game_id",
    ])
    .addCheckConstraint(
      "game_ratings_rating_check",
      sql`rating >= 1 and rating <= 5`
    )
    .execute();

  await db.schema
    .createIndex("game_ratings_user_id_idx")
    .ifNotExists()
    .on("game_ratings")
    .column("user_id")
    .execute();

  await db.schema
    .createIndex("game_ratings_game_id_idx")
    .ifNotExists()
    .on("game_ratings")
    .column("game_id")
    .execute();
};
