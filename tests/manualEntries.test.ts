import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";
import {
  normalizeGame,
  normalizeGameTitle,
} from "../src/domain/normalize-game";
import {
  getManualInputs,
  mapManualInput,
  startManualSession,
  stopManualSession,
} from "../src/application/manual";
import { ensureGameId } from "../src/infrastructure/db/queries";

const baseDatabaseUrl = Bun.env.DATABASE_URL;
const hasDatabase = Boolean(baseDatabaseUrl);
const requiresDb = hasDatabase ? test : test.skip;

let db: TestDatabase | null = null;
let userId = 0;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }

  db = await createTestDatabase();
  Bun.env.DATABASE_URL = db.databaseUrl;
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
    truncate table poll_results, play_sessions, manual_game_inputs, games, platform_credentials, users
    restart identity cascade
  `;

  const userRows = await db.sql<{ id: number }[]>`
    insert into users (email) values ('manual@test.com') returning id
  `;
  userId = userRows[0].id;
});

const createManualCredential = async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const rows = await db.sql<{ id: number }[]>`
    insert into platform_credentials (user_id, platform_id, api_key, account_id)
    values (${userId}, (select id from platforms where key = 'manual'), null, null)
    returning id
  `;
  return rows[0].id;
};

const createManualInput = async (title: string, mappedGameId?: number) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const canonicalTitle = normalizeGameTitle(title);
  const rows = await db.sql<{ id: number }[]>`
    insert into manual_game_inputs (user_id, raw_title, canonical_title, mapped_game_id)
    values (${userId}, ${title}, ${canonicalTitle}, ${mappedGameId ?? null})
    returning id
  `;
  return rows[0].id;
};

const createGame = async (
  platform: "steam" | "manual",
  title: string,
  externalId?: string
) => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  return ensureGameId(
    normalizeGame({
      platform,
      title,
      externalId,
    }),
    db.sql
  );
};

requiresDb(
  "startManualSession creates manual credential when missing",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    await startManualSession(userId, "Stardew Valley");
    const rows = await db.sql<{ id: number }[]>`
    select id from platform_credentials
    where user_id = ${userId}
      and platform_id = (select id from platforms where key = 'manual')
  `;

    expect(rows.length).toBe(1);
  }
);

requiresDb(
  "startManualSession updates manual input title for same canonical",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const inputId = await createManualInput("Halo  Infinite");
    await startManualSession(userId, "Halo Infinite");
    const rows = await db.sql<{ raw_title: string }[]>`
      select raw_title from manual_game_inputs where id = ${inputId}
    `;

    expect(rows[0].raw_title).toBe("Halo Infinite");
  }
);

requiresDb("startManualSession rejects empty title", async () => {
  await expect(startManualSession(userId, "   ")).rejects.toThrow(
    "Title is required."
  );
});

requiresDb(
  "startManualSession ends existing active manual sessions before inserting new",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const credentialId = await createManualCredential();
    const gameId = await createGame("manual", "Old Session");
    await db.sql`
      insert into play_sessions (platform_credential_id, game_id, started_at)
      values (${credentialId}, ${gameId}, now())
    `;

    await startManualSession(userId, "New Session");

    const rows = await db.sql<{ ended_at: Date | null }[]>`
      select ended_at from play_sessions
      where platform_credential_id = ${credentialId}
      order by started_at
    `;

    expect(rows.length).toBe(2);
    expect(rows[0].ended_at).not.toBeNull();
    expect(rows[1].ended_at).toBeNull();
  }
);

requiresDb(
  "startManualSession creates a game entry when input is unmapped",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    await startManualSession(userId, "Mario Kart");
    const rows = await db.sql<{ title: string; platform: string }[]>`
      select g.title, p.key as platform
      from games g
      join platforms p on p.id = g.platform_id
      where g.title = 'Mario Kart'
    `;

    expect(rows.length).toBe(1);
    expect(rows[0].platform).toBe("manual");
  }
);

requiresDb(
  "startManualSession uses mapped game id when manual input is mapped",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const mappedGameId = await createGame("steam", "Hades", "steam-123");
    await createManualInput("Hades", mappedGameId);

    await startManualSession(userId, "Hades");

    const rows = await db.sql<{ game_id: number }[]>`
      select game_id from play_sessions
      order by id desc limit 1
    `;

    expect(rows[0].game_id).toBe(mappedGameId);
  }
);

requiresDb(
  "startManualSession creates a new game when mapped game is deleted",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const mappedGameId = await createGame("steam", "Old Mapped", "steam-456");
    await createManualInput("Old Mapped", mappedGameId);
    await db.sql`
      delete from games where id = ${mappedGameId}
    `;

    await startManualSession(userId, "Old Mapped");

    const rows = await db.sql<{ platform: string }[]>`
      select p.key as platform
      from play_sessions ps
      join games g on g.id = ps.game_id
      join platforms p on p.id = g.platform_id
      order by ps.id desc limit 1
    `;

    expect(rows[0].platform).toBe("manual");
  }
);

requiresDb(
  "stopManualSession returns zero when no manual credential exists",
  async () => {
    const result = await stopManualSession(userId);
    expect(result.endedSessionCount).toBe(0);
  }
);

requiresDb("stopManualSession ends all active manual sessions", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const credentialId = await createManualCredential();
  const gameId = await createGame("manual", "Active Session");
  await db.sql`
    insert into play_sessions (platform_credential_id, game_id, started_at)
    values (${credentialId}, ${gameId}, now()),
           (${credentialId}, ${gameId}, now())
  `;

  const result = await stopManualSession(userId);
  expect(result.endedSessionCount).toBe(2);

  const rows = await db.sql<{ ended_at: Date | null }[]>`
    select ended_at from play_sessions where platform_credential_id = ${credentialId}
  `;
  expect(rows.every((row) => row.ended_at !== null)).toBe(true);
});

requiresDb("mapManualInput maps to a local game id", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const inputId = await createManualInput("Celeste");
  const gameId = await createGame("steam", "Celeste", "steam-789");

  const result = await mapManualInput(userId, inputId, {
    type: "local",
    gameId,
  });

  expect(result?.mapped_game_id).toBe(gameId);
});

requiresDb("mapManualInput maps to an IGDB entry", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const inputId = await createManualInput("Zelda");

  const result = await mapManualInput(userId, inputId, {
    type: "igdb",
    igdbId: "9999",
    title: "The Legend of Zelda",
    platform: null,
    coverUrl: "https://example.com/cover.png",
    platformId: 130,
  });

  expect(result?.mapped_game_id).toBeDefined();

  const rows = await db.sql<{ platform: string; external_id: string }[]>`
    select p.key as platform, g.external_id
    from games g
    join platforms p on p.id = g.platform_id
    where g.id = ${result?.mapped_game_id}
  `;
  expect(rows[0].platform).toBe("switch-1");
  expect(rows[0].external_id).toBe("9999");
});

requiresDb(
  "mapManualInput supports IGDB mapping without platform",
  async () => {
    if (!db) {
      throw new Error("Test database not initialized.");
    }

    const inputId = await createManualInput("Mystery Game");

    const result = await mapManualInput(userId, inputId, {
      type: "igdb",
      igdbId: "10001",
      title: "Mystery Game",
      platform: null,
      coverUrl: null,
    });

    const rows = await db.sql<{ platform: string }[]>`
    select p.key as platform
    from games g
    join platforms p on p.id = g.platform_id
    where g.id = ${result?.mapped_game_id}
  `;
    expect(rows[0].platform).toBe("unknown");
  }
);

requiresDb("mapManualInput returns null for missing input id", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const gameId = await createGame("steam", "Missing Target", "steam-missing");
  const result = await mapManualInput(userId, 9999, {
    type: "local",
    gameId,
  });

  expect(result).toBeNull();
});

requiresDb("getManualInputs returns mapped game data", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  const mappedGameId = await createGame("steam", "Control", "steam-444");
  await createManualInput("Control", mappedGameId);

  const inputs = await getManualInputs(userId);

  expect(inputs.length).toBe(1);
  expect(inputs[0].mappedGame?.title).toBe("Control");
  expect(inputs[0].mappedGame?.platform).toBe("steam");
});

requiresDb("canonical title collisions reuse manual inputs", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  await startManualSession(userId, "Halo   Infinite");
  await startManualSession(userId, "halo infinite");

  const rows = await db.sql<{ count: string; raw_title: string }[]>`
    select count(*)::text as count, max(raw_title) as raw_title
    from manual_game_inputs
  `;

  expect(rows[0].count).toBe("1");
  expect(rows[0].raw_title).toBe("halo infinite");
});

requiresDb("startManualSession leaves only one active session", async () => {
  if (!db) {
    throw new Error("Test database not initialized.");
  }

  await startManualSession(userId, "Session One");
  await startManualSession(userId, "Session Two");

  const rows = await db.sql<{ count: string }[]>`
    select count(*)::text as count
    from play_sessions
    where ended_at is null
  `;

  expect(rows[0].count).toBe("1");
});
