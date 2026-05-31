import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("user_settings")
    .ifNotExists()
    .addColumn("user_id", "bigint", (col) =>
      col.primaryKey().references("users.id").onDelete("cascade")
    )
    .addColumn("ignore_short_sessions_minutes", "integer", (col) =>
      col.notNull().defaultTo(0)
    )
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();
};
