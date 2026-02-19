import postgres from "postgres";

import { runMigrations } from "../src/common/db/migrations";
import {
  ensureGameId,
  insertPlaySession,
} from "../src/shared/db/queries";
import { normalizeGame } from "../src/modules/games";

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

const sql = postgres(databaseUrl, {
  max: 1,
  onnotice: (notice) => {
    if (notice.code === "42P07") {
      return;
    }
    console.warn(notice);
  },
});

try {
  await runMigrations({ sql });

  const seedGames = SEED_GAMES;
  if (!seedGames.length) {
    throw new Error("No seed games provided.");
  }

  await sql.begin(async (tx) => {
    await tx`
      select setval(
        pg_get_serial_sequence('platform_credentials', 'id'),
        coalesce((select max(id) from platform_credentials), 0),
        true
      )
    `;
    const userRows = await tx<{ id: number }[]>`
      insert into users (email)
      values (${SEED_EMAIL})
      on conflict (email)
      do update set updated_at = now()
      returning id
    `;
    const userId = userRows[0]?.id;
    if (!userId) {
      throw new Error("Failed to create or load seed user.");
    }

    const ensureCredential = async (
      platform: "steam" | "psn" | "playnite",
      accountId: string | null
    ) => {
      const existingRows = await tx<{ id: number }[]>`
        select id
        from platform_credentials
        where user_id = ${userId}
          and platform_id = (select id from platforms where key = ${platform})
        limit 1
      `;
      const existingId = existingRows[0]?.id;
      if (existingId) {
        await tx`
          update platform_credentials
          set active = false, updated_at = now()
          where id = ${existingId}
        `;
        return existingId;
      }

      const rows = await tx<{ id: number }[]>`
        insert into platform_credentials (user_id, platform_id, api_key, account_id, active)
        values (
          ${userId},
          (select id from platforms where key = ${platform}),
          ${null},
          ${accountId},
          ${false}
        )
        returning id
      `;
      return rows[0]?.id ?? null;
    };

    const steamId = await ensureCredential("steam", null);
    const psnId = await ensureCredential("psn", null);
    const playniteId = await ensureCredential("playnite", "local");

    if (!steamId || !psnId || !playniteId) {
      throw new Error("Failed to create platform credentials.");
    }

    await tx`
      delete from play_sessions
      where platform_credential_id in (${steamId}, ${psnId}, ${playniteId})
    `;

    const games = [];
    for (const seed of seedGames) {
      const normalized = normalizeGame({
        platform: seed.platform,
        title: seed.title,
        externalId: seed.externalId,
        capsuleUrl: seed.capsuleUrl,
      });
      const id = await ensureGameId(normalized, tx);
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
      await insertPlaySession(
        session.platformCredentialId,
        session.gameId,
        session.startedAt,
        session.endedAt,
        tx
      );
    }
  });

  console.log("Seed data loaded.");
} catch (error) {
  console.error("Failed to seed data.");
  console.error(error);
  process.exitCode = 1;
} finally {
  await sql.end();
}
