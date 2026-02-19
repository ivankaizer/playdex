import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import { normalizeGame } from "../src/modules/games";
import { accountsRepository } from "../src/modules/accounts/accounts.repository";
import { pollingRepository } from "../src/modules/polling/polling.repository";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;

let db: TestDatabase | null = null;
let credentialId = 0;
let userId = 0;

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
  userId = userRows[0].id;

  const credentialRows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform_id, api_key, account_id)
    values (
      ${userId},
      (select id from platforms where key = 'steam'),
      'key',
      'steam-id'
    )
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
  const gameId = await pollingRepository.ensureGameId(normalized, db.sql);
  await pollingRepository.recordPollResult(
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
  const gameId = await pollingRepository.ensureGameId(normalized, db.sql);

  const startedAt = new Date("2024-01-01T01:00:00Z");
  await pollingRepository.startPlaySession(
    credentialId,
    gameId,
    startedAt,
    db.sql
  );
  const active = await pollingRepository.fetchActivePlaySession(
    credentialId,
    db.sql
  );
  expect(active?.game_id).toBe(gameId);

  const endedAt = new Date("2024-01-01T02:00:00Z");
  await pollingRepository.endPlaySession(active!.id, endedAt, db.sql);
  const sessions = await accountsRepository.listAccountSessions(
    credentialId,
    userId,
    10,
    0,
    {}
  );

  expect(sessions.length).toBe(1);
  expect(sessions[0].ended_at).not.toBeNull();
  expect(sessions[0].title).toBe("Half-Life 2");
});

requiresDb("ensureGameId de-duplicates by canonical key", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const first = normalizeGame({
    platform: "steam",
    title: "Portal",
    externalId: "400",
  });
  const second = normalizeGame({
    platform: "steam",
    title: "Portal (Updated)",
    externalId: "400",
  });

  const firstId = await pollingRepository.ensureGameId(first, db.sql);
  const secondId = await pollingRepository.ensureGameId(second, db.sql);

  const rows = await db.sql<{ title: string }[]>`
    select title from games where id = ${firstId}
  `;

  expect(firstId).toBe(secondId);
  expect(rows[0].title).toBe("Portal (Updated)");
});

requiresDb(
  "listAccountSessions returns an empty list when none exist",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const sessions = await accountsRepository.listAccountSessions(
      credentialId,
      userId,
      10,
      0,
      {}
    );
    expect(sessions).toEqual([]);
  }
);

requiresDb("listAccountSessions enforces the 10-session limit", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const normalized = normalizeGame({
    platform: "steam",
    title: "Limit Test",
    externalId: "limit-1",
  });
  const gameId = await pollingRepository.ensureGameId(normalized, db.sql);

  const baseTime = Date.parse("2024-01-01T00:00:00Z");
  for (let index = 0; index < 12; index += 1) {
    const startedAt = new Date(baseTime + index * 60_000);
    const endedAt = new Date(baseTime + (index + 1) * 60_000);
    await pollingRepository.startPlaySession(
      credentialId,
      gameId,
      startedAt,
      db.sql
    );
    const active = await pollingRepository.fetchActivePlaySession(
      credentialId,
      db.sql
    );
    if (active) {
      await pollingRepository.endPlaySession(active.id, endedAt, db.sql);
    }
  }

  const sessions = await accountsRepository.listAccountSessions(
    credentialId,
    userId,
    10,
    0,
    {}
  );
  expect(sessions.length).toBe(10);
});

requiresDb("play session timestamps survive DST transitions", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const normalized = normalizeGame({
    platform: "steam",
    title: "DST Test",
    externalId: "dst-1",
  });
  const gameId = await pollingRepository.ensureGameId(normalized, db.sql);

  const startedAt = new Date("2024-03-10T01:55:00-08:00");
  await pollingRepository.startPlaySession(
    credentialId,
    gameId,
    startedAt,
    db.sql
  );
  const active = await pollingRepository.fetchActivePlaySession(
    credentialId,
    db.sql
  );
  if (!active) {
    throw new Error("Expected active session after starting.");
  }

  const endedAt = new Date("2024-03-10T03:05:00-07:00");
  await pollingRepository.endPlaySession(active.id, endedAt, db.sql);

  const rows = await db.sql<{ started_at: Date; ended_at: Date | null }[]>`
    select started_at, ended_at from play_sessions where id = ${active.id}
  `;

  expect(rows[0].started_at.toISOString()).toBe(startedAt.toISOString());
  expect(rows[0].ended_at?.toISOString()).toBe(endedAt.toISOString());
});
