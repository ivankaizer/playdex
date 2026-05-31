import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import type { PlatformClient } from "../src/domain/platform";
import { startPolling } from "../src/modules/polling/polling";

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
    insert into platform_credentials (user_id, platform_id, api_key, account_id)
    values (
      ${userRows[0].id},
      (select id from platforms where key = 'steam'),
      'key',
      'steam-id'
    )
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
      db: db.db,
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

requiresDb("startPolling is idempotent for repeated poll results", async () => {
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
    let completed = 0;
    void startPolling({
      db: db.db,
      loadAccounts,
      pollIntervalSeconds: 0,
      maxCycles: 2,
      onCycleComplete: () => {
        completed += 1;
        if (completed >= 2) {
          resolve();
        }
      },
    });
  });

  const sessionRows = await db.sql<{ count: string }[]>`
    select count(*) from play_sessions
  `;
  const activeRows = await db.sql<{ count: string }[]>`
    select count(*) from play_sessions where ended_at is null
  `;

  expect(Number(sessionRows[0].count)).toBe(1);
  expect(Number(activeRows[0].count)).toBe(1);
});

requiresDb("startPolling continues when one account fails", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const userRows = await db.sql<{ id: number }[]>`
    insert into users (email) values ('second@example.com') returning id
  `;
  const secondCredentialRows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform_id, api_key, account_id)
    values (
      ${userRows[0].id},
      (select id from platforms where key = 'steam'),
      'key',
      'steam-id-2'
    )
    returning id
  `;

  const successClient: PlatformClient = {
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

  const failureClient: PlatformClient = {
    platform: "steam",
    async getCurrentlyPlaying() {
      throw new Error("Steam API failed");
    },
  };

  const loadAccounts = async () => [
    {
      platformCredentialId: credentialId,
      account: { platform: "steam", accountId: "steam-id" },
      client: successClient,
    },
    {
      platformCredentialId: secondCredentialRows[0].id,
      account: { platform: "steam", accountId: "steam-id-2" },
      client: failureClient,
    },
  ];

  await new Promise<void>((resolve) => {
    void startPolling({
      db: db.db,
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
    select count(*) from play_sessions
  `;

  expect(Number(pollRows[0].count)).toBe(1);
  expect(Number(sessionRows[0].count)).toBe(1);
});

requiresDb("startPolling backs off after failures", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  let attempts = 0;
  const client: PlatformClient = {
    platform: "steam",
    async getCurrentlyPlaying() {
      attempts += 1;
      const error = new Error("rate limited") as Error & { status?: number };
      error.status = 429;
      throw error;
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
    let completed = 0;
    void startPolling({
      db: db.db,
      loadAccounts,
      pollIntervalSeconds: 0,
      maxCycles: 2,
      onCycleComplete: () => {
        completed += 1;
        if (completed >= 2) {
          resolve();
        }
      },
    });
  });

  expect(attempts).toBe(1);
});

requiresDb(
  "startPolling ends timed out playnite sessions even without incoming pings",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const userRows = await db.sql<{ id: number }[]>`
      insert into users (email) values ('playnite-timeout@test.com') returning id
    `;
    const playniteCredentialRows = await db.sql<{ id: number }[]>`
      insert into platform_credentials (user_id, platform_id, api_key, account_id)
      values (
        ${userRows[0].id},
        (select id from platforms where key = 'playnite'),
        'playnite-key',
        null
      )
      returning id
    `;
    const gameRows = await db.sql<{ id: number }[]>`
      insert into games (platform_id, title, external_id, canonical_key)
      values (
        (select id from platforms where key = 'steam'),
        'Timeout Game',
        'timeout-1',
        'steam:timeout-1'
      )
      returning id
    `;

    const startedAt = new Date("2024-01-01T00:00:00Z");
    const lastPingAt = new Date("2024-01-01T00:01:00Z");
    const sessionRows = await db.sql<{ id: number }[]>`
      insert into play_sessions (platform_credential_id, game_id, started_at, updated_at)
      values (
        ${playniteCredentialRows[0].id},
        ${gameRows[0].id},
        ${startedAt},
        ${lastPingAt}
      )
      returning id
    `;

    const loadAccounts = async () => [];

    await new Promise<void>((resolve) => {
      void startPolling({
        db: db.db,
        loadAccounts,
        pollIntervalSeconds: 60,
        maxCycles: 1,
        onCycleComplete: () => resolve(),
      });
    });

    const rows = await db.sql<{ ended_at: Date | null }[]>`
      select ended_at
      from play_sessions
      where id = ${sessionRows[0].id}
    `;

    expect(rows[0].ended_at).not.toBeNull();
    expect(rows[0].ended_at?.toISOString()).toBe(
      new Date(lastPingAt.getTime() + 3 * 60_000).toISOString()
    );
  }
);
