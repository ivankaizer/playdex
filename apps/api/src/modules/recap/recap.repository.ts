import { sql } from "kysely";

import { getDb, type DbExecutor } from "../../common/db/database";
import type { DbBigInt } from "../../common/db/schema";

export type RecapSessionRow = {
  id: DbBigInt;
  started_at: Date;
  ended_at: Date | null;
  game_id: DbBigInt;
  title: string;
  capsule_url: string | null;
  platform: string;
  game_platform: string;
  game_canonical_key: string;
};

export type RecapFirstSessionRow = {
  game_id: DbBigInt;
  first_started_at: Date;
  title: string;
  capsule_url: string | null;
  platform: string;
  game_platform: string;
  game_canonical_key: string;
};

export type RecapYearRow = {
  year: number;
};

type ManualInputMappingDbRow = {
  canonical_title: string;
  mapped_game_id: DbBigInt | null;
  mapped_title: string | null;
  mapped_platform: string | null;
  mapped_capsule_url: string | null;
  mapped_canonical_key: string | null;
};

// PostgreSQL interval extraction does not have a clear typed-builder equivalent.
const buildIgnoreShortSessionsFilter = () => sql<boolean>`
  not (
    coalesce(${sql.ref("us.ignore_short_sessions_minutes")}, 0) > 0
    and ${sql.ref("ps.ended_at")} is not null
    and extract(
      epoch from (${sql.ref("ps.ended_at")} - ${sql.ref("ps.started_at")})
    ) < (coalesce(${sql.ref("us.ignore_short_sessions_minutes")}, 0) * 60)
  )
`;

const loadManualInputMappings = async (
  userId: number,
  canonicalTitles: string[],
  db: DbExecutor = getDb()
): Promise<ManualInputMappingDbRow[]> => {
  if (canonicalTitles.length === 0) {
    return [];
  }

  return db
    .selectFrom("manual_game_inputs as mgi")
    .leftJoin("games as g", "g.id", "mgi.mapped_game_id")
    .leftJoin("platforms as gp", "gp.id", "g.platform_id")
    .select([
      "mgi.canonical_title",
      "mgi.mapped_game_id",
      "g.title as mapped_title",
      "gp.key as mapped_platform",
      "g.capsule_url as mapped_capsule_url",
      "g.canonical_key as mapped_canonical_key",
    ])
    .where("mgi.user_id", "=", String(userId))
    .where("mgi.canonical_title", "in", canonicalTitles)
    .execute();
};

export const recapRepository = {
  listRecapSessions: async (
    userId: number,
    startedAt?: Date | null,
    endedAt?: Date | null,
    db: DbExecutor = getDb()
  ): Promise<RecapSessionRow[]> => {
    let query = db
      .selectFrom("play_sessions as ps")
      .innerJoin(
        "platform_credentials as pc",
        "pc.id",
        "ps.platform_credential_id"
      )
      .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
      .innerJoin("games as g", "g.id", "ps.game_id")
      .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
      .innerJoin(
        "platforms as game_platform",
        "game_platform.id",
        "g.platform_id"
      )
      .select([
        "ps.id",
        "ps.started_at",
        "ps.ended_at",
        "g.id as game_id",
        "g.title",
        "g.capsule_url",
        "pc_platform.key as platform",
        "game_platform.key as game_platform",
        "g.canonical_key as game_canonical_key",
      ])
      .where("pc.user_id", "=", String(userId))
      .where(buildIgnoreShortSessionsFilter());

    if (startedAt) {
      query = query.where("ps.started_at", ">=", startedAt);
    }

    if (endedAt) {
      query = query.where("ps.started_at", "<", endedAt);
    }

    return query.orderBy("ps.started_at", "asc").execute();
  },

  listRecapYears: async (
    userId: number,
    db: DbExecutor = getDb()
  ): Promise<RecapYearRow[]> =>
    db
      .selectFrom("play_sessions as ps")
      .innerJoin(
        "platform_credentials as pc",
        "pc.id",
        "ps.platform_credential_id"
      )
      .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
      // PostgreSQL UTC year extraction is intentionally kept as a narrow fragment.
      .select(
        sql<number>`
          extract(year from ${sql.ref("ps.started_at")} at time zone 'utc')::int
        `.as("year")
      )
      .distinct()
      .where("pc.user_id", "=", String(userId))
      .where(buildIgnoreShortSessionsFilter())
      .orderBy("year", "desc")
      .execute(),

  listFirstSessionsByGame: async (
    userId: number,
    db: DbExecutor = getDb()
  ): Promise<RecapFirstSessionRow[]> =>
    db
      .selectFrom("play_sessions as ps")
      .innerJoin(
        "platform_credentials as pc",
        "pc.id",
        "ps.platform_credential_id"
      )
      .leftJoin("user_settings as us", "us.user_id", "pc.user_id")
      .innerJoin("games as g", "g.id", "ps.game_id")
      .innerJoin("platforms as pc_platform", "pc_platform.id", "pc.platform_id")
      .innerJoin(
        "platforms as game_platform",
        "game_platform.id",
        "g.platform_id"
      )
      .select((eb) => [
        "ps.game_id",
        eb.fn.min<Date>("ps.started_at").as("first_started_at"),
        "g.title",
        "g.capsule_url",
        "pc_platform.key as platform",
        "game_platform.key as game_platform",
        "g.canonical_key as game_canonical_key",
      ])
      .where("pc.user_id", "=", String(userId))
      .where(buildIgnoreShortSessionsFilter())
      .groupBy([
        "ps.game_id",
        "g.title",
        "g.capsule_url",
        "pc_platform.key",
        "game_platform.key",
        "g.canonical_key",
      ])
      .orderBy("first_started_at", "asc")
      .execute(),

  loadManualMappings: async (userId: number, canonicalTitles: string[]) =>
    (await loadManualInputMappings(userId, canonicalTitles)).map((row) => ({
      ...row,
      mapped_game_id: row.mapped_game_id ? Number(row.mapped_game_id) : null,
    })),
};
