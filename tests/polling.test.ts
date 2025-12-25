import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import type { PlatformClient } from "../src/services/platform";
import { startPolling } from "../src/polling";

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

requiresDb("startPolling records poll results and sessions", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const client: PlatformClient = {
    platform: "steam",
    async getCurrentlyPlaying() {
      return {
        platform: "steam",
        accountId: "steam-id",
        game: {
          platform: "steam",
          title: "Half-Life",
          externalId: "456",
        },
        raw: { ok: true },
      };
    },
  };

  const loadAccounts = async () => [
    {
      platformCredentialId: credentialId,
      account: { platform: "steam", accountId: "steam-id" },
      client,
    },
  ];

  await new Promise<void>((resolve) => {
    void startPolling({
      sql: db.sql,
      loadAccounts,
      pollIntervalSeconds: 60,
      maxCycles: 1,
      onCycleComplete: () => resolve(),
    });
  });

  const pollRows = await db.sql<{ count: string }[]>`
    select count(*) from poll_results
  `;
  const sessionRows = await db.sql<{ count: string }[]>`
    select count(*) from play_sessions where ended_at is null
  `;

  expect(Number(pollRows[0].count)).toBe(1);
  expect(Number(sessionRows[0].count)).toBe(1);
});
