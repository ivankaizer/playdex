import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import { normalizeGame } from "../src/domain/normalize-game";
import { ensureGameId } from "../src/infrastructure/db/queries";
import { syncPlaySession } from "../src/application/polling";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;

let db: TestDatabase | null = null;
let credentialId = 0;
let gameIdA = 0;
let gameIdB = 0;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }
  db = await createTestDatabase();
});

afterAll(async () => {
  if (db) {
    await db.cleanup();
  }
});

beforeEach(async () => {
  if (!db) {
    return;
  }

  await db.sql`
    truncate table poll_results, play_sessions, games, platform_credentials, users
    restart identity cascade
  `;

  const userRows = await db.sql<{ id: number }[]>`
    insert into users (email) values ('tester@example.com') returning id
  `;
  const credentialRows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform, api_key, account_id)
    values (${userRows[0].id}, 'steam', 'key', 'steam-id')
    returning id
  `;
  credentialId = credentialRows[0].id;

  gameIdA = await ensureGameId(
    normalizeGame({
      platform: "steam",
      title: "Game A",
      externalId: "111",
    }),
    db.sql
  );
  gameIdB = await ensureGameId(
    normalizeGame({
      platform: "steam",
      title: "Game B",
      externalId: "222",
    }),
    db.sql
  );
});

requiresDb("syncPlaySession starts a session when none exists", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  await syncPlaySession(db.sql, credentialId, gameIdA, new Date());
  const rows = await db.sql<{ ended_at: Date | null }[]>`
    select ended_at from play_sessions
  `;

  expect(rows.length).toBe(1);
  expect(rows[0].ended_at).toBeNull();
});

requiresDb("syncPlaySession ends an active session when no game", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const startTime = new Date("2024-01-01T00:00:00Z");
  await syncPlaySession(db.sql, credentialId, gameIdA, startTime);
  await syncPlaySession(
    db.sql,
    credentialId,
    null,
    new Date("2024-01-01T01:00:00Z")
  );

  const rows = await db.sql<{ ended_at: Date | null }[]>`
    select ended_at from play_sessions
  `;

  expect(rows.length).toBe(1);
  expect(rows[0].ended_at).not.toBeNull();
});

requiresDb("syncPlaySession switches sessions when game changes", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  await syncPlaySession(
    db.sql,
    credentialId,
    gameIdA,
    new Date("2024-01-01T00:00:00Z")
  );
  const switchTime = new Date("2024-01-01T01:00:00Z");
  await syncPlaySession(db.sql, credentialId, gameIdB, switchTime);

  const rows = await db.sql<
    {
      game_id: number;
      ended_at: Date | null;
    }[]
  >`
    select game_id, ended_at
    from play_sessions
    order by id
  `;

  expect(rows.length).toBe(2);
  expect(rows[0].game_id).toBe(gameIdA);
  expect(rows[0].ended_at?.toISOString()).toBe(switchTime.toISOString());
  expect(rows[1].game_id).toBe(gameIdB);
  expect(rows[1].ended_at).toBeNull();
});

requiresDb("syncPlaySession is idempotent for the same game", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const startedAt = new Date("2024-01-01T00:00:00Z");
  await syncPlaySession(db.sql, credentialId, gameIdA, startedAt);
  await syncPlaySession(
    db.sql,
    credentialId,
    gameIdA,
    new Date("2024-01-01T00:10:00Z")
  );

  const rows = await db.sql<{ started_at: Date; ended_at: Date | null }[]>`
    select started_at, ended_at from play_sessions
  `;

  expect(rows.length).toBe(1);
  expect(rows[0].ended_at).toBeNull();
  expect(rows[0].started_at.toISOString()).toBe(startedAt.toISOString());
});

requiresDb(
  "syncPlaySession ignores out-of-order timestamps when game is unchanged",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const startedAt = new Date("2024-01-01T01:00:00Z");
    await syncPlaySession(db.sql, credentialId, gameIdA, startedAt);
    await syncPlaySession(
      db.sql,
      credentialId,
      gameIdA,
      new Date("2024-01-01T00:30:00Z")
    );

    const rows = await db.sql<{ started_at: Date; ended_at: Date | null }[]>`
      select started_at, ended_at from play_sessions
    `;

    expect(rows.length).toBe(1);
    expect(rows[0].ended_at).toBeNull();
    expect(rows[0].started_at.toISOString()).toBe(startedAt.toISOString());
  }
);
