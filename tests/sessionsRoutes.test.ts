import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";
import jwt from "jsonwebtoken";

import { createServer } from "../src/presentation/server";
import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";

const baseDatabaseUrl = Bun.env.DATABASE_URL;
const baseJwtSecret = Bun.env.JWT_SECRET;
const hasDatabase = Boolean(baseDatabaseUrl);
const requiresDb = hasDatabase ? test : test.skip;
const jwtSecret = "sessions-test-secret";

let db: TestDatabase | null = null;
let app: ReturnType<typeof createServer> | null = null;
let token = "";
let userId = 0;
let credentialId = 0;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }

  db = await createTestDatabase();
  Bun.env.DATABASE_URL = db.databaseUrl;
  Bun.env.JWT_SECRET = jwtSecret;
  app = createServer();
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
    truncate table poll_results, play_sessions, games, platform_credentials, users
    restart identity cascade
  `;

  const userRows = await db.sql<{ id: number }[]>`
    insert into users (email) values ('sessions@test.com') returning id
  `;
  userId = userRows[0].id;
  token = jwt.sign({ sub: userId, email: "sessions@test.com" }, jwtSecret);

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

const createGame = async (title: string, externalId: string) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const canonicalKey = `steam:${externalId}`;
  const gameRows = await db.sql<{ id: number }[]>`
    insert into games (platform_id, title, external_id, canonical_key)
    values (
      (select id from platforms where key = 'steam'),
      ${title},
      ${externalId},
      ${canonicalKey}
    )
    returning id
  `;
  return gameRows[0].id;
};

const createSession = async (
  gameId: number,
  startedAt: Date,
  endedAt: Date
) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  await db.sql`
    insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
    values (${credentialId}, ${gameId}, ${startedAt}, ${endedAt})
  `;
};

const seedSessions = async () => {
  const gameOld = await createGame("Old Session", "old-1");
  const gameMid = await createGame("Mid Session", "mid-1");
  const gameNew = await createGame("New Session", "new-1");

  await createSession(
    gameOld,
    new Date("2024-01-01T00:00:00Z"),
    new Date("2024-01-01T01:00:00Z")
  );
  await createSession(
    gameNew,
    new Date("2024-12-03T00:00:00Z"),
    new Date("2024-12-03T00:30:00Z")
  );
  await createSession(
    gameMid,
    new Date("2024-01-02T00:00:00Z"),
    new Date("2024-01-02T03:00:00Z")
  );
};

const fetchSessions = async (query: string) => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  const response = await app.handle(
    new Request(`http://localhost/api/sessions?${query}`, {
      headers: {
        authorization: `Bearer ${token}`,
      },
    })
  );

  expect(response.status).toBe(200);
  const payload = (await response.json()) as {
    data: {
      sessions: { game: string }[];
      total: number;
      limit: number;
      offset: number;
    } | null;
    error: { error: string } | null;
  };
  expect(payload.error).toBeNull();
  if (!payload.data) {
    throw new Error("Missing sessions data.");
  }
  return payload.data;
};

requiresDb("sessions endpoint sorts by date", async () => {
  await seedSessions();

  const descending = await fetchSessions(
    "limit=10&offset=0&sort=date&direction=desc"
  );
  expect(descending.total).toBe(3);
  expect(descending.sessions.map((session) => session.game)).toEqual([
    "New Session",
    "Mid Session",
    "Old Session",
  ]);

  const ascending = await fetchSessions(
    "limit=10&offset=0&sort=date&direction=asc"
  );
  expect(ascending.sessions.map((session) => session.game)).toEqual([
    "Old Session",
    "Mid Session",
    "New Session",
  ]);
});

requiresDb("sessions endpoint sorts by duration across pages", async () => {
  await seedSessions();

  const pageOne = await fetchSessions(
    "limit=2&offset=0&sort=duration&direction=desc"
  );
  expect(pageOne.sessions.map((session) => session.game)).toEqual([
    "Mid Session",
    "Old Session",
  ]);

  const pageTwo = await fetchSessions(
    "limit=2&offset=2&sort=duration&direction=desc"
  );
  expect(pageTwo.sessions.map((session) => session.game)).toEqual([
    "New Session",
  ]);
});
