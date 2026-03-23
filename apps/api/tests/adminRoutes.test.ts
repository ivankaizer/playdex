import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";
import jwt from "jsonwebtoken";

import { createApp } from "../src/app/createApp";
import { encodeGameId } from "../src/common/utils/gameHash";
import { normalizeGame } from "../src/modules/games";
import { pollingRepository } from "../src/modules/polling/polling.repository";
import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;
const jwtSecret = "admin-routes-test-secret";

let db: TestDatabase | null = null;
let app: ReturnType<typeof createApp> | null = null;

const normalizeId = (value: unknown, field: string) => {
  const numeric = Number(value);
  if (!Number.isInteger(numeric) || numeric <= 0) {
    throw new Error(`${field} must be a positive integer id.`);
  }
  return numeric;
};

const sendAdminRequest = async (
  path: string,
  options: {
    token: string;
    method?: string;
    body?: unknown;
  }
) => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  const { token, method = "GET", body } = options;

  return app.handle(
    new Request(`http://localhost${path}`, {
      method,
      headers: {
        authorization: `Bearer ${token}`,
        ...(body === undefined ? {} : { "content-type": "application/json" }),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  );
};

const createUser = async (email: string, isAdmin = false) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const rows = await db.sql<{ id: number }[]>`
    insert into users (email, is_admin)
    values (${email}, ${isAdmin})
    returning id
  `;
  const id = normalizeId(rows[0]?.id, "user.id");
  const token = jwt.sign({ sub: id, email, type: "access" }, jwtSecret);
  return { id, token };
};

const createCredential = async (
  userId: number,
  platform: string,
  accountId: string,
  apiKey = "api-key",
  active = true
) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const rows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
    values (
      ${userId},
      (select id from platforms where key = ${platform}),
      ${apiKey},
      ${accountId},
      ${active}
    )
    returning id
  `;
  return normalizeId(rows[0]?.id, "platform_credentials.id");
};

const createGame = async (
  platform: string,
  title: string,
  externalId: string
) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const canonicalKey = `${platform}:${externalId}`;
  const rows = await db.sql<{ id: number }[]>`
    insert into games (platform_id, title, external_id, canonical_key)
    values (
      (select id from platforms where key = ${platform}),
      ${title},
      ${externalId},
      ${canonicalKey}
    )
    returning id
  `;
  return normalizeId(rows[0]?.id, "games.id");
};

const createSession = async (
  credentialId: number,
  gameId: number,
  startedAt: Date,
  endedAt: Date | null
) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const rows = await db.sql<{ id: number }[]>`
    insert into play_sessions (platform_credential_id, game_id, started_at, ended_at)
    values (${credentialId}, ${gameId}, ${startedAt}, ${endedAt})
    returning id
  `;
  return normalizeId(rows[0]?.id, "play_sessions.id");
};

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
    truncate table poll_results, play_sessions, games, platform_credentials, users
    restart identity cascade
  `;
});

requiresDb("admin routes require users.is_admin = true", async () => {
  const regularUser = await createUser("member@test.com", false);

  const response = await sendAdminRequest("/api/admin/sessions/recent", {
    token: regularUser.token,
  });

  expect(response.status).toBe(403);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string };
  };
  expect(payload.error.error).toBe("Admin access required.");
});

requiresDb("admin game search requires non-empty query", async () => {
  const admin = await createUser("admin@test.com", true);

  const response = await sendAdminRequest("/api/admin/games/search", {
    token: admin.token,
  });

  expect(response.status).toBe(400);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string };
  };
  expect(payload.error.error).toBe("query is required.");
});

requiresDb(
  "admin game search supports game id, title, and image url",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const admin = await createUser("admin@test.com", true);
    const gameId = await createGame("steam", "Half-Life", "hl-1");
    await db.sql`
      update games
      set capsule_url = 'https://images.example.com/half-life-cover.jpg'
      where id = ${gameId}
    `;

    const byIdResponse = await sendAdminRequest(
      `/api/admin/games/search?query=${encodeURIComponent(
        encodeGameId(gameId)
      )}`,
      {
        token: admin.token,
      }
    );
    expect(byIdResponse.status).toBe(200);
    const byIdPayload = (await byIdResponse.json()) as {
      data: { games: { id: string }[] } | null;
      error: { error: string } | null;
    };
    expect(byIdPayload.error).toBeNull();
    expect(
      byIdPayload.data?.games.some((game) => game.id === encodeGameId(gameId))
    ).toBe(true);

    const byTitleResponse = await sendAdminRequest(
      "/api/admin/games/search?query=Half-Life",
      {
        token: admin.token,
      }
    );
    expect(byTitleResponse.status).toBe(200);
    const byTitlePayload = (await byTitleResponse.json()) as {
      data: { games: { id: string }[] } | null;
      error: { error: string } | null;
    };
    expect(byTitlePayload.error).toBeNull();
    expect(
      byTitlePayload.data?.games.some(
        (game) => game.id === encodeGameId(gameId)
      )
    ).toBe(true);

    const byImageResponse = await sendAdminRequest(
      "/api/admin/games/search?query=half-life-cover.jpg",
      {
        token: admin.token,
      }
    );
    expect(byImageResponse.status).toBe(200);
    const byImagePayload = (await byImageResponse.json()) as {
      data: { games: { id: string }[] } | null;
      error: { error: string } | null;
    };
    expect(byImagePayload.error).toBeNull();
    expect(
      byImagePayload.data?.games.some(
        (game) => game.id === encodeGameId(gameId)
      )
    ).toBe(true);
  }
);

requiresDb(
  "admin recent sessions returns last 3 sessions per user",
  async () => {
    const admin = await createUser("admin@test.com", true);
    const firstUser = await createUser("a@test.com");
    const secondUser = await createUser("b@test.com");

    const firstCredential = await createCredential(
      firstUser.id,
      "steam",
      "first-user"
    );
    const secondCredential = await createCredential(
      secondUser.id,
      "psn",
      "second-user"
    );

    const game1 = await createGame("steam", "Game 1", "g1");
    const game2 = await createGame("steam", "Game 2", "g2");
    const game3 = await createGame("steam", "Game 3", "g3");
    const game4 = await createGame("steam", "Game 4", "g4");
    const game5 = await createGame("psn", "Game 5", "g5");
    const game6 = await createGame("psn", "Game 6", "g6");

    await createSession(
      firstCredential,
      game1,
      new Date("2025-01-01T00:00:00Z"),
      new Date("2025-01-01T00:30:00Z")
    );
    await createSession(
      firstCredential,
      game2,
      new Date("2025-01-02T00:00:00Z"),
      new Date("2025-01-02T01:00:00Z")
    );
    await createSession(
      firstCredential,
      game3,
      new Date("2025-01-03T00:00:00Z"),
      new Date("2025-01-03T00:15:00Z")
    );
    await createSession(
      firstCredential,
      game4,
      new Date("2025-01-04T00:00:00Z"),
      new Date("2025-01-04T00:45:00Z")
    );
    await createSession(
      secondCredential,
      game5,
      new Date("2025-01-05T00:00:00Z"),
      new Date("2025-01-05T00:10:00Z")
    );
    await createSession(
      secondCredential,
      game6,
      new Date(Date.now() - 120_000),
      null
    );

    const response = await sendAdminRequest("/api/admin/sessions/recent", {
      token: admin.token,
    });

    expect(response.status).toBe(200);
    const payload = (await response.json()) as {
      data: {
        users: {
          email: string;
          sessions: {
            gameName: string;
            live: boolean;
            durationSeconds: number;
          }[];
        }[];
      } | null;
      error: { error: string } | null;
    };
    expect(payload.error).toBeNull();

    const users = payload.data?.users ?? [];
    const first = users.find((user) => user.email === "a@test.com");
    const second = users.find((user) => user.email === "b@test.com");

    expect(first?.sessions.map((session) => session.gameName)).toEqual([
      "Game 4",
      "Game 3",
      "Game 2",
    ]);
    expect(first?.sessions[0]?.live).toBe(false);
    expect(first?.sessions[0]?.durationSeconds).toBe(2700);
    expect(second?.sessions.some((session) => session.live)).toBe(true);
  }
);

requiresDb(
  "admin can toggle platform providers and update credential fields",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const admin = await createUser("admin@test.com", true);
    const userA = await createUser("user-a@test.com");
    const userB = await createUser("user-b@test.com");

    const credentialA = await createCredential(userA.id, "steam", "steam-a");
    await createCredential(userB.id, "steam", "steam-b");
    await createCredential(userB.id, "psn", "psn-b");

    const toggleResponse = await sendAdminRequest(
      "/api/admin/providers/platforms/status",
      {
        token: admin.token,
        method: "PUT",
        body: { platform: "steam", enabled: false },
      }
    );
    expect(toggleResponse.status).toBe(200);

    const steamRows = await db.sql<{ active: boolean }[]>`
      select pc.active
      from platform_credentials pc
      join platforms p on p.id = pc.platform_id
      where p.key = 'steam'
    `;
    expect(steamRows.every((row) => row.active === false)).toBe(true);

    const updateResponse = await sendAdminRequest(
      `/api/admin/credentials/${credentialA}`,
      {
        token: admin.token,
        method: "PUT",
        body: {
          apiKey: "updated-key",
          user: "updated-user",
        },
      }
    );
    expect(updateResponse.status).toBe(200);

    const updatedCredential = await db.sql<
      { api_key: string | null; account_id: string | null }[]
    >`
      select api_key, account_id
      from platform_credentials
      where id = ${credentialA}
    `;
    expect(updatedCredential[0].api_key).toBe("updated-key");
    expect(updatedCredential[0].account_id).toBe("updated-user");
  }
);

requiresDb("admin can move multiple sessions between accounts", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const admin = await createUser("admin@test.com", true);
  const user = await createUser("player@test.com");
  const sourceCredential = await createCredential(
    user.id,
    "steam",
    "source-account"
  );
  const targetCredential = await createCredential(
    user.id,
    "psn",
    "target-account"
  );
  const firstGame = await createGame("steam", "First Move Game", "move-1");
  const secondGame = await createGame("steam", "Second Move Game", "move-2");

  const firstSessionId = await createSession(
    sourceCredential,
    firstGame,
    new Date("2025-01-10T10:00:00Z"),
    new Date("2025-01-10T11:00:00Z")
  );
  const secondSessionId = await createSession(
    sourceCredential,
    secondGame,
    new Date("2025-01-10T12:00:00Z"),
    new Date("2025-01-10T13:00:00Z")
  );

  const moveResponse = await sendAdminRequest("/api/admin/sessions/move", {
    token: admin.token,
    method: "PUT",
    body: {
      sessionIds: [firstSessionId, secondSessionId],
      targetCredentialId: targetCredential,
    },
  });
  expect(moveResponse.status).toBe(200);

  const movePayload = (await moveResponse.json()) as {
    data: { movedSessions: number; targetCredentialId: number } | null;
    error: { error: string } | null;
  };
  expect(movePayload.error).toBeNull();
  expect(movePayload.data?.movedSessions).toBe(2);
  expect(movePayload.data?.targetCredentialId).toBe(targetCredential);

  const movedRows = await db.sql<
    { id: number | string; platform_credential_id: number | string }[]
  >`
      select id, platform_credential_id
      from play_sessions
      where id in (${firstSessionId}, ${secondSessionId})
      order by id
    `;

  const normalizedMovedRows = movedRows.map((row) => ({
    id: normalizeId(row.id, "play_sessions.id"),
    platform_credential_id: normalizeId(
      row.platform_credential_id,
      "play_sessions.platform_credential_id"
    ),
  }));

  expect(normalizedMovedRows).toEqual([
    { id: firstSessionId, platform_credential_id: targetCredential },
    { id: secondSessionId, platform_credential_id: targetCredential },
  ]);
});

requiresDb("admin game image update is preserved on poll upsert", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const admin = await createUser("admin@test.com", true);

  const initial = normalizeGame({
    platform: "steam",
    title: "Portal",
    externalId: "portal-1",
    capsuleUrl: "https://images.example.com/original.jpg",
  });
  const gameId = normalizeId(
    await pollingRepository.ensureGameId(initial, db.sql),
    "games.id"
  );

  const updateResponse = await sendAdminRequest("/api/admin/games", {
    token: admin.token,
    method: "PUT",
    body: {
      gameId: encodeGameId(gameId),
      imageUrl: "https://images.example.com/custom.jpg",
    },
  });
  expect(updateResponse.status).toBe(200);

  const nextPoll = normalizeGame({
    platform: "steam",
    title: "Portal",
    externalId: "portal-1",
    capsuleUrl: "https://images.example.com/from-poll.jpg",
  });
  await pollingRepository.ensureGameId(nextPoll, db.sql);

  const rows = await db.sql<
    { capsule_url: string | null; capsule_url_locked: boolean }[]
  >`
    select capsule_url, capsule_url_locked
    from games
    where id = ${gameId}
  `;

  expect(rows[0].capsule_url).toBe("https://images.example.com/custom.jpg");
  expect(rows[0].capsule_url_locked).toBe(true);
});

requiresDb("admin game title update is preserved on poll upsert", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const admin = await createUser("admin@test.com", true);

  const initial = normalizeGame({
    platform: "steam",
    title: "Original Title",
    externalId: "title-1",
    capsuleUrl: "https://images.example.com/title-original.jpg",
  });
  const gameId = normalizeId(
    await pollingRepository.ensureGameId(initial, db.sql),
    "games.id"
  );

  const updateResponse = await sendAdminRequest("/api/admin/games", {
    token: admin.token,
    method: "PUT",
    body: {
      gameId: encodeGameId(gameId),
      title: "Custom Title",
      lockTitle: true,
    },
  });
  expect(updateResponse.status).toBe(200);

  const nextPoll = normalizeGame({
    platform: "steam",
    title: "Polled New Title",
    externalId: "title-1",
    capsuleUrl: "https://images.example.com/title-poll.jpg",
  });
  await pollingRepository.ensureGameId(nextPoll, db.sql);

  const rows = await db.sql<{ title: string; title_locked: boolean }[]>`
    select title, title_locked
    from games
    where id = ${gameId}
  `;

  expect(rows[0].title).toBe("Custom Title");
  expect(rows[0].title_locked).toBe(true);
});

requiresDb("admin game update accepts gameId from query string", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const admin = await createUser("admin@test.com", true);
  const gameId = await createGame("steam", "Query Route", "query-route-1");
  const encoded = encodeGameId(gameId);

  const response = await sendAdminRequest(
    `/api/admin/games?gameId=${encodeURIComponent(encoded)}`,
    {
      token: admin.token,
      method: "PUT",
      body: {
        title: "Query Route Updated",
      },
    }
  );

  expect(response.status).toBe(200);
  const rows = await db.sql<{ title: string }[]>`
    select title from games where id = ${gameId}
  `;
  expect(rows[0].title).toBe("Query Route Updated");
});
