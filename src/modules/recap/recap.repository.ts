import type { Sql } from "postgres";

import { getSql } from "../../shared/db/postgres";
import { loadManualInputMappings } from "../../shared/db/queries";

export type RecapSessionRow = {
  id: number;
  started_at: Date;
  ended_at: Date | null;
  game_id: number;
  title: string;
  capsule_url: string | null;
  platform: string;
  game_platform: string;
  game_canonical_key: string;
};

export type RecapFirstSessionRow = {
  game_id: number;
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

const buildIgnoreShortSessionsFilter = (sql: Sql) => sql`
  and not (
    coalesce(us.ignore_short_sessions_minutes, 0) > 0
    and ps.ended_at is not null
    and extract(epoch from (ps.ended_at - ps.started_at)) <
      (coalesce(us.ignore_short_sessions_minutes, 0) * 60)
  )
`;

const buildRangeFilters = (
  sql: Sql,
  startedAt?: Date | null,
  endedAt?: Date | null
) => {
  const fromFilter = startedAt ? sql`and ps.started_at >= ${startedAt}` : sql``;
  const toFilter = endedAt ? sql`and ps.started_at < ${endedAt}` : sql``;
  return { fromFilter, toFilter };
};

export const recapRepository = {
  listRecapSessions: async (
    userId: number,
    startedAt?: Date | null,
    endedAt?: Date | null,
    sql: Sql = getSql()
  ) => {
    const { fromFilter, toFilter } = buildRangeFilters(sql, startedAt, endedAt);
    return sql<RecapSessionRow[]>`
      select
        ps.id,
        ps.started_at,
        ps.ended_at,
        g.id as game_id,
        g.title,
        g.capsule_url,
        pc_platform.key as platform,
        game_platform.key as game_platform,
        g.canonical_key as game_canonical_key
      from play_sessions ps
      join platform_credentials pc on pc.id = ps.platform_credential_id
      left join user_settings us on us.user_id = pc.user_id
      join games g on g.id = ps.game_id
      join platforms pc_platform on pc_platform.id = pc.platform_id
      join platforms game_platform on game_platform.id = g.platform_id
      where pc.user_id = ${userId}
        ${fromFilter}
        ${toFilter}
        ${buildIgnoreShortSessionsFilter(sql)}
      order by ps.started_at asc
    `;
  },

  listRecapYears: async (userId: number, sql: Sql = getSql()) =>
    sql<RecapYearRow[]>`
      select distinct extract(year from ps.started_at at time zone 'utc')::int as year
      from play_sessions ps
      join platform_credentials pc on pc.id = ps.platform_credential_id
      left join user_settings us on us.user_id = pc.user_id
      where pc.user_id = ${userId}
        ${buildIgnoreShortSessionsFilter(sql)}
      order by year desc
    `,

  listFirstSessionsByGame: async (userId: number, sql: Sql = getSql()) =>
    sql<RecapFirstSessionRow[]>`
      select
        ps.game_id,
        min(ps.started_at) as first_started_at,
        g.title,
        g.capsule_url,
        pc_platform.key as platform,
        game_platform.key as game_platform,
        g.canonical_key as game_canonical_key
      from play_sessions ps
      join platform_credentials pc on pc.id = ps.platform_credential_id
      left join user_settings us on us.user_id = pc.user_id
      join games g on g.id = ps.game_id
      join platforms pc_platform on pc_platform.id = pc.platform_id
      join platforms game_platform on game_platform.id = g.platform_id
      where pc.user_id = ${userId}
        ${buildIgnoreShortSessionsFilter(sql)}
      group by
        ps.game_id,
        g.title,
        g.capsule_url,
        pc_platform.key,
        game_platform.key,
        g.canonical_key
      order by first_started_at asc
    `,

  loadManualMappings: (userId: number, canonicalTitles: string[]) =>
    loadManualInputMappings(userId, canonicalTitles),
};
