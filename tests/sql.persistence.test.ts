import {
  afterAll,
  beforeAll,
  beforeEach,
  expect,
  test,
} from "bun:test";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import { normalizeGame } from "../src/services/normalize";
import {
  endPlaySession,
  ensureGameId,
  fetchActivePlaySession,
  listAccountSessions,
  recordPollResult,
  startPlaySession,
} from "../src/sql/queries";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;

let db: TestDatabase | null = null;
let credentialId = 0;

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
});

requiresDb("ensureGameId and recordPollResult persist data", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const normalized = normalizeGame({
    platform: "steam",
    title: "Half-Life",
    externalId: "456",
  });
  const gameId = await ensureGameId(normalized, db.sql);
  await recordPollResult(
    credentialId,
    gameId,
    new Date("2024-01-01T00:00:00Z"),
    { sample: true },
    db.sql
  );

  const rows = await db.sql<{ count: string }[]>`
    select count(*) from poll_results
  `;
  expect(Number(rows[0].count)).toBe(1);
});

requiresDb("play sessions are persisted and listed", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const normalized = normalizeGame({
    platform: "steam",
    title: "Half-Life 2",
    externalId: "789",
  });
  const gameId = await ensureGameId(normalized, db.sql);

  const startedAt = new Date("2024-01-01T01:00:00Z");
  await startPlaySession(credentialId, gameId, startedAt, db.sql);
  const active = await fetchActivePlaySession(credentialId, db.sql);
  expect(active?.game_id).toBe(gameId);

  const endedAt = new Date("2024-01-01T02:00:00Z");
  await endPlaySession(active!.id, endedAt, db.sql);
  const sessions = await listAccountSessions(credentialId, db.sql);

  expect(sessions.length).toBe(1);
  expect(sessions[0].ended_at).not.toBeNull();
  expect(sessions[0].title).toBe("Half-Life 2");
});
