import { sql } from "kysely";

import { createDb, ensureDatabaseExists } from "../src/common/db/database";
import { runMigrations } from "../src/common/db/migrations";
import { upsertGame } from "../src/common/db/queries/games";
import { normalizeGame } from "../src/modules/games";
import type { DbExecutor } from "../src/common/db/database";

const databaseUrl = Bun.env.DATABASE_URL;
if (!databaseUrl?.trim()) {
  console.error("DATABASE_URL is required.");
  process.exit(1);
}

const SEED_EMAIL = "fake@playdex.local";

type GameSeed = {
  platform: string;
  title: string;
  externalId?: string;
  capsuleUrl?: string;
};

type SessionSeed = {
  platformCredentialId: number;
  gameId: number;
  startedAt: Date;
  endedAt: Date;
};

const SEED_GAMES: GameSeed[] = [
  {
    platform: "steam",
    externalId: "1771300",
    title: "Kingdom Come: Deliverance II",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/1771300/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "3159330",
    title: "Assassin's Creed Shadows",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/3159330/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "668580",
    title: "Atomic Heart",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/668580/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "2186680",
    title: "Warhammer 40,000: Rogue Trader",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/2186680/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "920210",
    title: "LEGO® Star Wars™: The Skywalker Saga",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/920210/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "502500",
    title: "ACE COMBAT™7: SKIES UNKNOWN",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/502500/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "1547000",
    title: "Grand Theft Auto: San Andreas - The Definitive Edition",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/1547000/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "2375550",
    title: "Like a Dragon Gaiden: The Man Who Erased His Name",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/2375550/library_600x900_2x.jpg",
  },
  {
    platform: "steam",
    externalId: "1857090",
    title: "Norland",
    capsuleUrl:
      "https://shared.steamstatic.com/store_item_assets/steam/apps/1857090/library_600x900_2x.jpg",
  },
  {
    platform: "unknown",
    externalId: "1",
    title: "League of Legends",
    capsuleUrl:
      "https://encrypted-tbn0.gstatic.com/images?q=tbn:ANd9GcTI29PK-OT68a3Jx-n5U0Fi4rvZNv3mYxZKXDOnE2BbiJEqH0ofqVtBto_ofYVkXrZptHZT&s=10",
  },
  {
    platform: "psn",
    externalId: "10009568",
    title: "Battlefield™ 6",
    capsuleUrl:
      "https://image.api.playstation.com/pr/bam-art/222/196/8294c8a7-ec75-4d7d-9bd4-380edbd32b00.jpg?w=940&thumb=false",
  },
  {
    platform: "psn",
    externalId: "10011468",
    title: "Dying Light: The Beast",
    capsuleUrl:
      "https://image.api.playstation.com/pr/bam-art/222/201/41a78531-c1fd-48fa-90c5-03bedd61a2c6.jpg?w=440",
  },
];

const randomInt = (min: number, max: number) =>
  Math.floor(Math.random() * (max - min + 1)) + min;

const pick = <T>(values: T[]) => values[randomInt(0, values.length - 1)];

const buildSession = (
  gameId: number,
  platformCredentialId: number,
  year: number,
  monthIndex: number,
  day: number
): SessionSeed => {
  const durationMinutes = randomInt(30, 90);
  const hour = randomInt(8, 22);
  const minute = randomInt(0, 59);
  const startedAt = new Date(Date.UTC(year, monthIndex, day, hour, minute, 0));
  const endedAt = new Date(startedAt.getTime() + durationMinutes * 60 * 1000);

  return {
    platformCredentialId,
    gameId,
    startedAt,
    endedAt,
  };
};

const loadPlatformId = async (db: DbExecutor, key: string) => {
  const row = await db
    .selectFrom("platforms")
    .select("id")
    .where("key", "=", key)
    .limit(1)
    .executeTakeFirst();
  return row?.id;
};

try {
  await ensureDatabaseExists(databaseUrl);
} catch (error) {
  console.error("Database setup failed.");
  console.error(error);
  process.exit(1);
}

const db = createDb(databaseUrl, { max: 1 });

try {
  await runMigrations({ db });

  const seedGames = SEED_GAMES;
  if (!seedGames.length) {
    throw new Error("No seed games provided.");
  }

  await db.transaction().execute(async (trx) => {
    const platformCredentialsId = await loadPlatformId(
      trx,
      "platform_credentials"
    );

    // Align the platform_credentials sequence to avoid id conflicts.
    if (platformCredentialsId) {
      await sql`
        select setval(
          pg_get_serial_sequence('platform_credentials', 'id'),
          coalesce((select max(id) from platform_credentials), 0),
          true
        )
      `.execute(trx);
    }

    const userRow = await trx
      .insertInto("users")
      .values({ email: SEED_EMAIL })
      .onConflict((oc) =>
        oc.column("email").doUpdateSet({
          updated_at: sql<Date>`now()`,
        })
      )
      .returning("id")
      .executeTakeFirst();

    const userId = userRow?.id;
    if (!userId) {
      throw new Error("Failed to create or load seed user.");
    }

    const ensureCredential = async (
      platform: "steam" | "psn" | "playnite",
      accountId: string | null
    ) => {
      const platformId = await loadPlatformId(trx, platform);

      const existingRow = await trx
        .selectFrom("platform_credentials")
        .select("id")
        .where("user_id", "=", String(userId))
        .where("platform_id", "=", String(platformId!))
        .limit(1)
        .executeTakeFirst();

      const existingId = existingRow?.id;
      if (existingId) {
        await trx
          .updateTable("platform_credentials")
          .set({
            active: false,
            updated_at: sql<Date>`now()`,
          })
          .where("id", "=", String(existingId))
          .execute();
        return Number(existingId);
      }

      const rows = await trx
        .insertInto("platform_credentials")
        .values({
          user_id: userId,
          platform_id: platformId!,
          api_key: null,
          account_id: accountId,
          active: false,
        })
        .returning("id")
        .execute();

      return rows[0]?.id ? Number(rows[0].id) : null;
    };

    const steamId = await ensureCredential("steam", null);
    const psnId = await ensureCredential("psn", null);
    const playniteId = await ensureCredential("playnite", "local");

    if (!steamId || !psnId || !playniteId) {
      throw new Error("Failed to create platform credentials.");
    }

    // Delete existing play sessions for the seed credentials.
    await trx
      .deleteFrom("play_sessions")
      .where(
        "platform_credential_id",
        "in",
        [steamId, psnId, playniteId].map(String)
      )
      .execute();

    const games: { id: number; platform: string }[] = [];
    for (const seed of seedGames) {
      const normalized = normalizeGame({
        platform: seed.platform,
        title: seed.title,
        externalId: seed.externalId,
        capsuleUrl: seed.capsuleUrl,
      });
      const id = await upsertGame(trx, normalized);
      games.push({ id, platform: normalized.platform });
    }

    const pickCredentialId = (platform: string) => {
      if (platform === "steam") {
        return steamId;
      }
      if (platform === "psn") {
        return psnId;
      }
      return playniteId;
    };

    const sessions: SessionSeed[] = [];
    const now = new Date();
    const seedYear = now.getUTCFullYear();
    const seedMonth = now.getUTCMonth();
    const seedDay = now.getUTCDate();
    const sessionCount = randomInt(6, 10);

    for (let index = 0; index < sessionCount; index += 1) {
      const game = pick(games);
      sessions.push(
        buildSession(
          game.id,
          pickCredentialId(game.platform),
          seedYear,
          seedMonth,
          seedDay
        )
      );
    }

    for (const session of sessions) {
      await trx
        .insertInto("play_sessions")
        .values({
          platform_credential_id: session.platformCredentialId,
          game_id: session.gameId,
          started_at: session.startedAt,
          ended_at: session.endedAt,
        })
        .execute();
    }
  });

  console.log("Seed data loaded.");
} catch (error) {
  console.error("Failed to seed data.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await db.destroy();
}
