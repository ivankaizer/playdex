import { sql, type Kysely } from "kysely";

export const up = async (db: Kysely<unknown>) => {
  await db.schema
    .createTable("platforms")
    .ifNotExists()
    .addColumn("id", "bigserial", (col) => col.primaryKey())
    .addColumn("key", "text", (col) => col.notNull().unique())
    .addColumn("name", "text", (col) => col.notNull())
    .addColumn("igdb_platform_id", "integer")
    .addColumn("parent_id", "bigint", (col) =>
      col.references("platforms.id").onDelete("set null")
    )
    .addColumn("created_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .addColumn("updated_at", "timestamptz", (col) =>
      col.notNull().defaultTo(sql`now()`)
    )
    .execute();

  // Preserve the historical ordered backfill as one frozen PostgreSQL-specific
  // fragment: it relies on UPDATE FROM, scalar subqueries, and lower() remapping.
  await sql`
    insert into platforms (key, name, igdb_platform_id)
    values
      ('ps5', 'PS5', 167),
      ('xbox-series-xs', 'Xbox Series X|S', 169),
      ('pc', 'PC', 6),
      ('switch-1', 'Nintendo Switch 1', 130),
      ('switch-2', 'Nintendo Switch 2', 508),
      ('unknown', 'Unknown', null);

    insert into platforms (key, name, parent_id)
    values
      ('steam', 'Steam', (select id from platforms where key = 'pc')),
      ('playnite', 'Playnite', (select id from platforms where key = 'pc')),
      ('psn', 'PSN', (select id from platforms where key = 'ps5')),
      ('manual', 'Manual', null)
  `.execute(db);

  await db.schema
    .alterTable("platform_credentials")
    .addColumn("platform_id", "bigint", (col) => col.ifNotExists())
    .execute();

  await db.schema
    .alterTable("games")
    .addColumn("platform_id", "bigint", (col) => col.ifNotExists())
    .execute();

  // See the comment above. These statements intentionally mirror the legacy
  // SQL so existing platform mappings remain byte-for-byte equivalent.
  await sql`
    update platform_credentials pc
    set platform_id = p.id
    from platforms p
    where pc.platform_id is null
      and p.key = pc.platform;

    update platform_credentials
    set platform_id = (select id from platforms where key = 'unknown')
    where platform_id is null;

    update games g
    set platform_id = p.id
    from platforms p
    where g.platform_id is null
      and p.key = g.platform;

    update games
    set platform_id = (select id from platforms where key = 'ps5')
    where platform_id is null
      and lower(platform) in ('ps5', 'playstation 5', 'playstation5', 'ps 5');

    update games
    set platform_id = (select id from platforms where key = 'xbox-series-xs')
    where platform_id is null
      and lower(platform) in (
        'xbox series x|s',
        'xbox series x',
        'xbox series s',
        'xbox series xs',
        'xbox series'
      );

    update games
    set platform_id = (select id from platforms where key = 'pc')
    where platform_id is null
      and lower(platform) in ('pc', 'windows');

    update games
    set platform_id = (select id from platforms where key = 'switch-1')
    where platform_id is null
      and lower(platform) in (
        'nintendo switch',
        'switch',
        'switch 1',
        'nintendo switch 1'
      );

    update games
    set platform_id = (select id from platforms where key = 'switch-2')
    where platform_id is null
      and lower(platform) in ('switch 2', 'nintendo switch 2');

    update games
    set platform_id = (select id from platforms where key = 'steam')
    where platform_id is null
      and lower(platform) = 'steam';

    update games
    set platform_id = (select id from platforms where key = 'playnite')
    where platform_id is null
      and lower(platform) = 'playnite';

    update games
    set platform_id = (select id from platforms where key = 'ps5')
    where platform_id is null
      and lower(platform) = 'psn';

    update games
    set platform_id = (select id from platforms where key = 'manual')
    where platform_id is null
      and lower(platform) = 'manual';

    update games
    set platform_id = (select id from platforms where key = 'unknown')
    where platform_id is null
      and lower(platform) = 'unknown';

    update games
    set platform_id = (select id from platforms where key = 'unknown')
    where platform_id is null
      and lower(platform) = 'igdb';

    update games
    set platform_id = (select id from platforms where key = 'unknown')
    where platform_id is null
  `.execute(db);

  await db.schema
    .alterTable("platform_credentials")
    .alterColumn("platform_id", (col) => col.setNotNull())
    .execute();

  await db.schema
    .alterTable("games")
    .alterColumn("platform_id", (col) => col.setNotNull())
    .execute();

  await db.schema
    .alterTable("platform_credentials")
    .addForeignKeyConstraint(
      "platform_credentials_platform_id_fkey",
      ["platform_id"],
      "platforms",
      ["id"]
    )
    .execute();

  await db.schema
    .alterTable("games")
    .addForeignKeyConstraint(
      "games_platform_id_fkey",
      ["platform_id"],
      "platforms",
      ["id"]
    )
    .execute();

  await db.schema
    .alterTable("platform_credentials")
    .dropColumn("platform")
    .execute();

  await db.schema.alterTable("games").dropColumn("platform").execute();
};
