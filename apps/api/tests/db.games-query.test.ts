import { afterAll, beforeAll, beforeEach, expect, test } from "bun:test";

import type { NormalizedGame } from "../src/common/domain/game";
import { upsertGame } from "../src/common/db/queries/games";
import type { TestDatabase } from "./helpers/db";
import { createTestDatabase } from "./helpers/db";

const hasDatabase = Boolean(Bun.env.DATABASE_URL);
const requiresDb = hasDatabase ? test : test.skip;

const game = (overrides: Partial<NormalizedGame> = {}): NormalizedGame => ({
  platform: "steam",
  title: "Portal",
  externalId: "400",
  capsuleUrl: "https://images.example.com/portal.jpg",
  canonicalTitle: "portal",
  canonicalKey: "steam:400",
  ...overrides,
});

let database: TestDatabase | null = null;

beforeAll(async () => {
  if (!hasDatabase) {
    return;
  }

  database = await createTestDatabase();
});

afterAll(async () => {
  if (database) {
    await database.cleanup();
  }
});

beforeEach(async () => {
  if (!database) {
    return;
  }

  await database.sql`truncate table games restart identity cascade`;
});

requiresDb(
  "refreshes unlocked game metadata inside a transaction",
  async () => {
    if (!database) {
      throw new Error("Test database not initialized.");
    }

    const initialId = await upsertGame(database.db, game());
    const refreshedId = await database.db.transaction().execute((transaction) =>
      upsertGame(
        transaction,
        game({
          title: "Portal Updated",
          capsuleUrl: "https://images.example.com/portal-updated.jpg",
        })
      )
    );

    const stored = await database.db
      .selectFrom("games")
      .select(["title", "capsule_url"])
      .where("id", "=", initialId)
      .executeTakeFirstOrThrow();

    expect(refreshedId).toBe(initialId);
    expect(stored).toEqual({
      title: "Portal Updated",
      capsule_url: "https://images.example.com/portal-updated.jpg",
    });
  }
);

requiresDb("preserves a locked title during an upsert", async () => {
  if (!database) {
    throw new Error("Test database not initialized.");
  }

  const gameId = await upsertGame(database.db, game());
  await database.db
    .updateTable("games")
    .set({ title_locked: true })
    .where("id", "=", gameId)
    .execute();

  await upsertGame(
    database.db,
    game({
      title: "Portal Updated",
      capsuleUrl: "https://images.example.com/portal-updated.jpg",
    })
  );

  const stored = await database.db
    .selectFrom("games")
    .select(["title", "capsule_url"])
    .where("id", "=", gameId)
    .executeTakeFirstOrThrow();

  expect(stored).toEqual({
    title: "Portal",
    capsule_url: "https://images.example.com/portal-updated.jpg",
  });
});

requiresDb("preserves a locked capsule URL during an upsert", async () => {
  if (!database) {
    throw new Error("Test database not initialized.");
  }

  const gameId = await upsertGame(database.db, game());
  await database.db
    .updateTable("games")
    .set({ capsule_url_locked: true })
    .where("id", "=", gameId)
    .execute();

  await upsertGame(
    database.db,
    game({
      title: "Portal Updated",
      capsuleUrl: "https://images.example.com/portal-updated.jpg",
    })
  );

  const stored = await database.db
    .selectFrom("games")
    .select(["title", "capsule_url"])
    .where("id", "=", gameId)
    .executeTakeFirstOrThrow();

  expect(stored).toEqual({
    title: "Portal Updated",
    capsule_url: "https://images.example.com/portal.jpg",
  });
});

requiresDb(
  "keeps an existing capsule URL when an incoming URL is absent",
  async () => {
    if (!database) {
      throw new Error("Test database not initialized.");
    }

    const gameId = await upsertGame(database.db, game());
    await upsertGame(
      database.db,
      game({
        title: "Portal Updated",
        capsuleUrl: undefined,
      })
    );

    const stored = await database.db
      .selectFrom("games")
      .select(["title", "capsule_url"])
      .where("id", "=", gameId)
      .executeTakeFirstOrThrow();

    expect(stored).toEqual({
      title: "Portal Updated",
      capsule_url: "https://images.example.com/portal.jpg",
    });
  }
);

requiresDb(
  "uses the canonical key as the persisted game identity",
  async () => {
    if (!database) {
      throw new Error("Test database not initialized.");
    }

    const initialId = await upsertGame(database.db, game());
    const refreshedId = await upsertGame(
      database.db,
      game({
        platform: "playnite",
        externalId: "playnite-game-id",
      })
    );

    const stored = await database.db
      .selectFrom("games")
      .innerJoin("platforms", "platforms.id", "games.platform_id")
      .select([
        "games.canonical_key",
        "games.external_id",
        "platforms.key as platform",
      ])
      .where("games.id", "=", initialId)
      .executeTakeFirstOrThrow();

    expect(refreshedId).toBe(initialId);
    expect(stored).toEqual({
      canonical_key: "steam:400",
      external_id: "playnite-game-id",
      platform: "playnite",
    });
  }
);

requiresDb("falls back to the unknown platform identity", async () => {
  if (!database) {
    throw new Error("Test database not initialized.");
  }

  const gameId = await upsertGame(
    database.db,
    game({
      platform: "unregistered-provider",
      canonicalKey: "unregistered-provider:400",
    })
  );

  const stored = await database.db
    .selectFrom("games")
    .innerJoin("platforms", "platforms.id", "games.platform_id")
    .select("platforms.key as platform")
    .where("games.id", "=", gameId)
    .executeTakeFirstOrThrow();

  expect(stored.platform).toBe("unknown");
});
