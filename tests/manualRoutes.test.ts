import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";
import jwt from "jsonwebtoken";

import { createApp } from "../src/app/createApp";
import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import { normalizeGameTitle } from "../src/modules/games";

const baseDatabaseUrl = Bun.env.DATABASE_URL;
const baseJwtSecret = Bun.env.JWT_SECRET;
const hasDatabase = Boolean(baseDatabaseUrl);
const requiresDb = hasDatabase ? test : test.skip;
const jwtSecret = "manual-routes-secret";

let db: TestDatabase | null = null;
let app: ReturnType<typeof createApp> | null = null;
let token = "";
let userId = 0;
let manualInputId = 0;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }

  db = await createTestDatabase();
  Bun.env.DATABASE_URL = db.databaseUrl;
  Bun.env.JWT_SECRET = jwtSecret;
  app = createApp();
});

afterAll(async () => {
  if (baseDatabaseUrl) {
    Bun.env.DATABASE_URL = baseDatabaseUrl;
  } else {
    delete Bun.env.DATABASE_URL;
  }

  if (baseJwtSecret) {
    Bun.env.JWT_SECRET = baseJwtSecret;
  } else {
    delete Bun.env.JWT_SECRET;
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
    truncate table poll_results, play_sessions, manual_game_inputs, games, platform_credentials, users
    restart identity cascade
  `;

  const userRows = await db.sql<{ id: number }[]>`
    insert into users (email) values ('manual-route@test.com') returning id
  `;
  userId = userRows[0].id;
  token = jwt.sign(
    { sub: userId, email: "manual-route@test.com", type: "access" },
    jwtSecret
  );

  const canonicalTitle = normalizeGameTitle("Map Target");
  const inputRows = await db.sql<{ id: number }[]>`
    insert into manual_game_inputs (user_id, raw_title, canonical_title)
    values (${userId}, 'Map Target', ${canonicalTitle})
    returning id
  `;
  manualInputId = inputRows[0].id;
});

const sendMapRequest = async (body: Record<string, unknown>) => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  return await app.handle(
    new Request(`http://localhost/api/manual/inputs/${manualInputId}/map`, {
      method: "POST",
      headers: {
        Authorization: `Bearer ${token}`,
        "Content-Type": "application/json",
      },
      body: JSON.stringify(body),
    })
  );
};

requiresDb(
  "manual mapping rejects missing igdbId for igdb selections",
  async () => {
    const response = await sendMapRequest({
      title: "Missing IGDB",
      platformKey: "switch-1",
    });

    expect(response.status).toBe(400);
    const payload = (await response.json()) as {
      error: { error: string } | null;
    };
    expect(payload.error?.error).toBe("igdbId and title are required.");
  }
);

requiresDb(
  "manual mapping rejects missing title for igdb selections",
  async () => {
    const response = await sendMapRequest({
      igdbId: "12345",
      platformKey: "switch-1",
    });

    expect(response.status).toBe(400);
    const payload = (await response.json()) as {
      error: { error: string } | null;
    };
    expect(payload.error?.error).toBe("igdbId and title are required.");
  }
);
