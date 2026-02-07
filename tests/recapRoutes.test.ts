import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";
import jwt from "jsonwebtoken";

import { createApp } from "../src/app/createApp";
import { decodeGameHash } from "../src/common/utils/gameHash";
import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;
const jwtSecret = "recap-routes-test-secret";

let db: TestDatabase | null = null;
let app: ReturnType<typeof createApp> | null = null;
let token = "";
let userId = 0;
let credentialId = 0;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }

  db = await createTestDatabase();
  Bun.env.JWT_SECRET = jwtSecret;
  app = createApp();
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
    truncate table
      poll_results,
      play_sessions,
      game_connections,
      games,
      platform_credentials,
      users
    restart identity cascade
  `;

  const userRows = await db.sql<{ id: number }[]>`
    insert into users (email) values ('recap@test.com') returning id
  `;
  userId = Number(userRows[0].id);
  token = jwt.sign(
    { sub: userId, email: "recap@test.com", type: "access" },
    jwtSecret
  );

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
  credentialId = Number(credentialRows[0].id);
});

const createGame = async (title: string, externalId: string) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const canonicalKey = `steam:${externalId}`;
  const rows = await db.sql<{ id: number }[]>`
    insert into games (platform_id, title, external_id, canonical_key)
    values (
      (select id from platforms where key = 'steam'),
      ${title},
      ${externalId},
      ${canonicalKey}
    )
    returning id
  `;
  return Number(rows[0].id);
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

const fetchRecap = async (query = "") => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  const suffix = query ? `?${query}` : "";
  return await app.handle(
    new Request(`http://localhost/api/recap${suffix}`, {
      headers: {
        authorization: `Bearer ${token}`,
      },
    })
  );
};

const fetchRecapYears = async () => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  return await app.handle(
    new Request("http://localhost/api/recap/years", {
      headers: {
        authorization: `Bearer ${token}`,
      },
    })
  );
};

requiresDb("recap years returns distinct years ordered desc", async () => {
  const gameId = await createGame("Year Sample", "year-sample");
  await createSession(
    gameId,
    new Date("2023-06-10T10:00:00Z"),
    new Date("2023-06-10T11:00:00Z")
  );
  await createSession(
    gameId,
    new Date("2024-02-05T10:00:00Z"),
    new Date("2024-02-05T11:00:00Z")
  );
  await createSession(
    gameId,
    new Date("2024-10-01T10:00:00Z"),
    new Date("2024-10-01T11:00:00Z")
  );

  const response = await fetchRecapYears();
  expect(response.status).toBe(200);

  const payload = (await response.json()) as {
    data: { years: number[] } | null;
    error: { error: string } | null;
  };
  expect(payload.error).toBeNull();
  expect(payload.data?.years).toEqual([2024, 2023]);
});

requiresDb(
  "recap range validation rejects invalid from/to combinations",
  async () => {
    const missingTo = await fetchRecap("from=2024-01-01");
    expect(missingTo.status).toBe(400);
    const missingToPayload = (await missingTo.json()) as {
      error: { error: string } | null;
    };
    expect(missingToPayload.error?.error).toBe("from and to are required.");

    const invalidDate = await fetchRecap("from=invalid&to=2024-01-01");
    expect(invalidDate.status).toBe(400);
    const invalidDatePayload = (await invalidDate.json()) as {
      error: { error: string } | null;
    };
    expect(invalidDatePayload.error?.error).toBe(
      "from and to must be valid dates."
    );

    const invalidOrder = await fetchRecap("from=2024-01-05&to=2024-01-01");
    expect(invalidOrder.status).toBe(400);
    const invalidOrderPayload = (await invalidOrder.json()) as {
      error: { error: string } | null;
    };
    expect(invalidOrderPayload.error?.error).toBe("from must be before to.");
  }
);

requiresDb(
  "recap applies custom range and returns encoded game ids",
  async () => {
    const gameInRangeId = await createGame("In Range", "in-range");
    const gameOutOfRangeId = await createGame("Out Of Range", "out-of-range");

    await createSession(
      gameInRangeId,
      new Date("2024-01-01T00:00:00Z"),
      new Date("2024-01-01T01:00:00Z")
    );
    await createSession(
      gameInRangeId,
      new Date("2024-01-02T23:30:00Z"),
      new Date("2024-01-03T00:00:00Z")
    );
    await createSession(
      gameOutOfRangeId,
      new Date("2024-01-03T00:05:00Z"),
      new Date("2024-01-03T01:05:00Z")
    );

    const response = await fetchRecap("from=2024-01-01&to=2024-01-02");
    expect(response.status).toBe(200);

    const payload = (await response.json()) as {
      data: {
        range: { from: string | null; to: string | null };
        totalSessions: number;
        topGameByTime: { gameId: string; title: string } | null;
        topGamesByTime: { gameId: string; title: string }[];
        gamesTable: { gameId: string; title: string }[];
        mostUsedPlatform: {
          platform: string;
          totalSeconds: number;
          sessionCount: number;
        } | null;
      } | null;
      error: { error: string } | null;
    };

    expect(payload.error).toBeNull();
    if (!payload.data || !payload.data.topGameByTime) {
      throw new Error("Missing recap data.");
    }

    expect(payload.data.range).toEqual({
      from: "2024-01-01",
      to: "2024-01-02",
    });
    expect(payload.data.totalSessions).toBeGreaterThan(0);
    expect(payload.data.topGameByTime.title).toBe("In Range");
    expect(decodeGameHash(payload.data.topGameByTime.gameId)).toBe(
      gameInRangeId
    );

    const topGameIds = payload.data.topGamesByTime
      .map((game) => decodeGameHash(game.gameId))
      .filter((gameId): gameId is number => gameId !== null);
    expect(topGameIds).toContain(gameInRangeId);
    expect(topGameIds).not.toContain(gameOutOfRangeId);

    const tableGameIds = payload.data.gamesTable
      .map((game) => decodeGameHash(game.gameId))
      .filter((gameId): gameId is number => gameId !== null);
    expect(tableGameIds).toContain(gameInRangeId);
    expect(tableGameIds).not.toContain(gameOutOfRangeId);

    expect(payload.data.mostUsedPlatform?.platform).toBe("steam");
    expect(payload.data.mostUsedPlatform?.sessionCount ?? 0).toBeGreaterThan(0);
  }
);
