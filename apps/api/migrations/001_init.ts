import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("users")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("email", "text", (col) => col.notNull().unique())
    .addColumn("password", "text")
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();

  await db.schema
    .createTable("platform_credentials")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("user_id", "bigint", (col) => col.notNull())
    .addColumn("platform", "text", (col) => col.notNull())
    .addColumn("api_key", "text")
    .addColumn("account_id", "text")
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addUniqueConstraint("platform_credentials_user_api_account_key", [
      "user_id",
      "api_key",
      "account_id",
    ])
    .addForeignKeyConstraint(
      "platform_credentials_user_id_fkey",
      ["user_id"],
      "users",
      ["id"],
      (constraint) => constraint.onDelete("cascade")
    )
    .execute();

  await db.schema
    .createTable("games")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("platform", "text", (col) => col.notNull())
    .addColumn("title", "text", (col) => col.notNull())
    .addColumn("external_id", "text")
    .addColumn("canonical_key", "text", (col) => col.notNull().unique())
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();

  await db.schema
    .createTable("play_sessions")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("platform_credential_id", "bigint", (col) =>
      col.notNull().references("platform_credentials.id").onDelete("cascade")
    )
    .addColumn("game_id", "bigint", (col) =>
      col.notNull().references("games.id").onDelete("cascade")
    )
    .addColumn("started_at", "timestamptz", (col) => col.notNull())
    .addColumn("ended_at", "timestamptz")
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();
};
