import { SteamClient } from "../src/modules/providers/clients/steam-client";

const apiKey = Bun.env.STEAM_API_KEY;
const steamId = Bun.env.STEAM_ID;

if (!apiKey || !apiKey.trim()) {
  console.error("Missing STEAM_API_KEY.");
  process.exit(1);
}

if (!steamId || !steamId.trim()) {
  console.error("Missing STEAM_ID.");
  process.exit(1);
}

const client = new SteamClient({ apiKey, steamId });

const run = async () => {
  const result = await client.getCurrentlyPlaying({
    platform: "steam",
    accountId: steamId,
  });

  const game = result.game;
  if (!game) {
    console.log("No game currently playing.");
    return;
  }

  console.log(`${game.title} (${game.externalId ?? "unknown id"})`);
};

run().catch((error) => {
  console.error("Failed to fetch current Steam game.");
  console.error(error);
  process.exit(1);
});
