import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import { createServer } from "../src/presentation/server";
import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";

const baseDatabaseUrl = Bun.env.DATABASE_URL;
const hasDatabase = Boolean(baseDatabaseUrl);
const requiresDb = hasDatabase ? test : test.skip;

let db: TestDatabase | null = null;
let app: ReturnType<typeof createServer> | null = null;
let credentialId = 0;
const apiKey = "playnite-key";

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }

  db = await createTestDatabase();
  Bun.env.DATABASE_URL = db.databaseUrl;
  app = createServer();
});

afterAll(async () => {
  if (baseDatabaseUrl) {
    Bun.env.DATABASE_URL = baseDatabaseUrl;
  } else {
    delete Bun.env.DATABASE_URL;
  }

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
    insert into users (email) values ('playnite@test.com') returning id
  `;
  const credentialRows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform, api_key, account_id)
    values (${userRows[0].id}, 'playnite', ${apiKey}, null)
    returning id
  `;
  credentialId = credentialRows[0].id;
});

const sendRequest = async (body: Record<string, unknown>, token?: string) => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  return await app.handle(
    new Request("http://localhost/api/playnite/sessions", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        ...(token ? { authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(body),
    })
  );
};

requiresDb("Playnite session endpoint inserts a completed session", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const response = await sendRequest(
    {
      durationInSeconds: 120,
      gameTitle: "Hades",
      gameId: "123",
      gameProvider: "steam",
    },
    apiKey
  );

  expect(response.status).toBe(200);
  const payload = (await response.json()) as {
    data: { sessionId: string } | null;
    error: { error: string } | null;
  };
  expect(payload.error).toBeNull();
  expect(typeof payload.data?.sessionId).toBe("string");

  const rows = await db.sql<
    {
      platform_credential_id: number;
      started_at: Date;
      ended_at: Date;
      platform: string;
      title: string;
      external_id: string | null;
    }[]
  >`
    select
      ps.platform_credential_id,
      ps.started_at,
      ps.ended_at,
      g.platform,
      g.title,
      g.external_id
    from play_sessions ps
    join games g on g.id = ps.game_id
  `;

  expect(rows.length).toBe(1);
  expect(rows[0].platform_credential_id).toBe(credentialId);
  expect(rows[0].platform).toBe("steam");
  expect(rows[0].title).toBe("Hades");
  expect(rows[0].external_id).toBe("123");

  const duration =
    rows[0].ended_at.getTime() - rows[0].started_at.getTime();
  expect(Math.round(duration / 1000)).toBe(120);
});

requiresDb("Playnite session endpoint accepts unknown provider", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const response = await sendRequest(
    {
      durationInSeconds: 75,
      gameTitle: "Some Indie",
      gameId: "unknown-42",
      gameProvider: "unknown",
    },
    apiKey
  );

  expect(response.status).toBe(200);
  const payload = (await response.json()) as {
    data: { sessionId: string } | null;
    error: { error: string } | null;
  };
  expect(payload.error).toBeNull();
  expect(typeof payload.data?.sessionId).toBe("string");

  const rows = await db.sql<
    {
      platform_credential_id: number;
      platform: string;
      title: string;
      external_id: string | null;
    }[]
  >`
    select
      ps.platform_credential_id,
      g.platform,
      g.title,
      g.external_id
    from play_sessions ps
    join games g on g.id = ps.game_id
  `;

  expect(rows.length).toBe(1);
  expect(rows[0].platform_credential_id).toBe(credentialId);
  expect(rows[0].platform).toBe("unknown");
  expect(rows[0].title).toBe("Some Indie");
  expect(rows[0].external_id).toBe("unknown-42");
});

requiresDb("Playnite session endpoint rejects missing api key", async () => {
  const response = await sendRequest({
    durationInSeconds: 120,
    gameTitle: "Hades",
    gameId: "123",
    gameProvider: "steam",
  });

  expect(response.status).toBe(401);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string } | null;
  };
  expect(payload.data).toBeNull();
  expect(payload.error?.error).toBe("Missing api key.");
});

requiresDb("Playnite session endpoint rejects invalid provider", async () => {
  const response = await sendRequest(
    {
      durationInSeconds: 120,
      gameTitle: "Hades",
      gameId: "123",
      gameProvider: "psn",
    },
    apiKey
  );

  expect(response.status).toBe(400);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string } | null;
  };
  expect(payload.data).toBeNull();
  expect(payload.error?.error).toBe("gameProvider must be steam or unknown.");
});

requiresDb("Playnite session endpoint rejects invalid api key", async () => {
  const response = await sendRequest(
    {
      durationInSeconds: 120,
      gameTitle: "Hades",
      gameId: "123",
      gameProvider: "steam",
    },
    "bad-key"
  );

  expect(response.status).toBe(401);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string } | null;
  };
  expect(payload.data).toBeNull();
  expect(payload.error?.error).toBe("Invalid api key.");
});
