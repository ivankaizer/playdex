import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("poll_results")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("platform_credential_id", "bigint", (col) =>
      col.notNull().references("platform_credentials.id").onDelete("cascade")
    )
    .addColumn("game_id", "bigint", (col) =>
      col.references("games.id").onDelete("set null")
    )
    .addColumn("polled_at", "timestamptz", (col) => col.notNull())
    .addColumn("raw_payload", "jsonb")
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();

  await db.schema
    .createIndex("poll_results_platform_credential_id_idx")
    .ifNotExists()
    .on("poll_results")
    .column("platform_credential_id")
    .execute();

  await db.schema
    .createIndex("poll_results_polled_at_idx")
    .ifNotExists()
    .on("poll_results")
    .column("polled_at")
    .execute();
};
