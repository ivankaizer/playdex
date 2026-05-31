import { sql } from "kysely";

// PostgreSQL interval extraction does not have a clear typed-builder equivalent.
export const ignoreShortSessionsFilter = () => sql<boolean>`
  not (
    coalesce(${sql.ref("us.ignore_short_sessions_minutes")}, 0) > 0
    and ${sql.ref("ps.ended_at")} is not null
    and extract(
      epoch from (${sql.ref("ps.ended_at")} - ${sql.ref("ps.started_at")})
    ) < (coalesce(${sql.ref("us.ignore_short_sessions_minutes")}, 0) * 60)
  )
`;

export const durationSecondsExpression = () =>
  sql<number>`
    greatest(
      0,
      floor(
        extract(
          epoch
          from (coalesce(${sql.ref("ps.ended_at")}, now()) - ${sql.ref("ps.started_at")})
        )
      )
    )::integer
  `;

export const durationSortExpression = () =>
  sql<number>`extract(epoch from (coalesce(${sql.ref("ps.ended_at")}, now()) - ${sql.ref("ps.started_at")}))`;

export const ignoredCaseExpression = () =>
  sql<boolean>`case
    when coalesce(${sql.ref("us.ignore_short_sessions_minutes")}, 0) > 0
      and ${sql.ref("ps.ended_at")} is not null
      and extract(epoch from (${sql.ref("ps.ended_at")} - ${sql.ref("ps.started_at")}))
        < (coalesce(${sql.ref("us.ignore_short_sessions_minutes")}, 0) * 60)
      then true
    else false
  end`;

export const totalSecondsExpression = () =>
  sql<number>`
    coalesce(
      sum(extract(epoch from (coalesce(${sql.ref("ps.ended_at")}, now()) - ${sql.ref("ps.started_at")}))),
      0
    )::float
  `;

export const sessionCountExpression = () => sql<number>`count(*)::int`;

export const utcDayStartExpression = () =>
  sql<Date>`(date_trunc('day', now() at time zone 'utc') at time zone 'utc')`;

export const utcDayEndExpression = () =>
  sql<Date>`(date_trunc('day', now() at time zone 'utc') at time zone 'utc') + interval '1 day'`;
