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
    new Request("http://localhost/playnite/sessions", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        ...(token ? { authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(body),
    })
  );
};

const sendStartRequest = async (
  body: Record<string, unknown>,
  token?: string
) => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  return await app.handle(
    new Request("http://localhost/playnite/sessions/start", {
      method: "POST",
      headers: {
        "content-type": "application/json",
        ...(token ? { authorization: `Bearer ${token}` } : {}),
      },
      body: JSON.stringify(body),
    })
  );
};

const sendEndRequest = async (token?: string) => {
  if (!app) {
    throw new Error("Server not initialized.");
  }

  return await app.handle(
    new Request("http://localhost/playnite/sessions/end", {
      method: "POST",
      headers: {
        ...(token ? { authorization: `Bearer ${token}` } : {}),
      },
    })
  );
};

const createCredential = async (key: string) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const userRows = await db.sql<{ id: number }[]>`
    select id from users limit 1
  `;
  const credentialRows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform, api_key, account_id)
    values (${userRows[0].id}, 'playnite', ${key}, null)
    returning id
  `;
  return credentialRows[0].id;
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
  const gameRows = await db.sql<{ id: number }[]>`
    insert into games (platform, title, external_id, canonical_key)
    values (${platform}, ${title}, ${externalId}, ${canonicalKey})
    returning id
  `;
  return gameRows[0].id;
};

const createActiveSession = async (
  platformCredentialId: number,
  gameId: number,
  startedAt = new Date("2024-01-01T00:00:00Z")
) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  await db.sql`
    insert into play_sessions (platform_credential_id, game_id, started_at)
    values (${platformCredentialId}, ${gameId}, ${startedAt})
  `;
};

requiresDb(
  "Playnite session start endpoint ends existing sessions and creates a new one",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const otherCredentialId = await createCredential("playnite-other");
    const oldGameId = await createGame("steam", "Old Game", "old-1");
    const otherGameId = await createGame("steam", "Other Game", "other-1");
    await createActiveSession(credentialId, oldGameId);
    await createActiveSession(otherCredentialId, otherGameId);

    const response = await sendStartRequest(
      {
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
      { platform_credential_id: number; ended_at: Date | null }[]
    >`
      select platform_credential_id, ended_at
      from play_sessions
      order by id
    `;

    const activeForCredential = rows.filter(
      (row) =>
        row.platform_credential_id === credentialId && row.ended_at === null
    );
    const endedForCredential = rows.filter(
      (row) =>
        row.platform_credential_id === credentialId && row.ended_at !== null
    );
    const activeForOther = rows.filter(
      (row) =>
        row.platform_credential_id === otherCredentialId &&
        row.ended_at === null
    );

    expect(activeForCredential.length).toBe(1);
    expect(endedForCredential.length).toBe(1);
    expect(activeForOther.length).toBe(1);
  }
);

requiresDb("Playnite session start endpoint rejects missing api key", async () => {
  const response = await sendStartRequest({
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

requiresDb("Playnite session start endpoint rejects invalid provider", async () => {
  const response = await sendStartRequest(
    {
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

requiresDb("Playnite session start endpoint rejects invalid api key", async () => {
  const response = await sendStartRequest(
    {
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

requiresDb(
  "Playnite session end endpoint stops active sessions for the api key",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const otherCredentialId = await createCredential("playnite-other");
    const oldGameId = await createGame("steam", "Old Game", "old-1");
    const otherGameId = await createGame("steam", "Other Game", "other-1");
    await createActiveSession(credentialId, oldGameId);
    await createActiveSession(otherCredentialId, otherGameId);

    const response = await sendEndRequest(apiKey);

    expect(response.status).toBe(200);
    const payload = (await response.json()) as {
      data: { endedSessionCount: number } | null;
      error: { error: string } | null;
    };
    expect(payload.error).toBeNull();
    expect(payload.data?.endedSessionCount).toBe(1);

    const rows = await db.sql<
      { platform_credential_id: number; ended_at: Date | null }[]
    >`
      select platform_credential_id, ended_at
      from play_sessions
      order by id
    `;

    const endedForCredential = rows.filter(
      (row) =>
        row.platform_credential_id === credentialId && row.ended_at !== null
    );
    const activeForOther = rows.filter(
      (row) =>
        row.platform_credential_id === otherCredentialId &&
        row.ended_at === null
    );

    expect(endedForCredential.length).toBe(1);
    expect(activeForOther.length).toBe(1);
  }
);

requiresDb(
  "Playnite session end endpoint returns zero when no sessions are active",
  async () => {
    const response = await sendEndRequest(apiKey);

    expect(response.status).toBe(200);
    const payload = (await response.json()) as {
      data: { endedSessionCount: number } | null;
      error: { error: string } | null;
    };
    expect(payload.error).toBeNull();
    expect(payload.data?.endedSessionCount).toBe(0);
  }
);

requiresDb("Playnite session end endpoint rejects missing api key", async () => {
  const response = await sendEndRequest();

  expect(response.status).toBe(401);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string } | null;
  };
  expect(payload.data).toBeNull();
  expect(payload.error?.error).toBe("Missing api key.");
});

requiresDb("Playnite session end endpoint rejects invalid api key", async () => {
  const response = await sendEndRequest("bad-key");

  expect(response.status).toBe(401);
  const payload = (await response.json()) as {
    data: null;
    error: { error: string } | null;
  };
  expect(payload.data).toBeNull();
  expect(payload.error?.error).toBe("Invalid api key.");
});
