import { PsnClient } from "../src/infrastructure/clients/psn-client";

const npsso = Bun.env.PSN_NPSSO;
const accountId = Bun.env.PSN_ACCOUNT_ID;
if (!npsso || !npsso.trim()) {
  console.error("Missing PSN_NPSSO.");
  process.exit(1);
}

const client = new PsnClient({ npsso });

const run = async () => {
  let resolvedAccountId = accountId?.trim() ?? "";

  if (!resolvedAccountId) {
    const resolved = await client.resolveAccountId();
    if (!resolved) {
      console.error("Could not resolve account ID from access token.");
      process.exit(1);
    }

    resolvedAccountId = resolved;
    console.log(`Resolved PSN account ID: ${resolvedAccountId}`);
  }

  const result = await client.getCurrentlyPlaying({
    platform: "psn",
    accountId: resolvedAccountId,
  });

  const game = result.game;
  if (!game) {
    console.log("No game currently playing.");
    return;
  }

  console.log(`${game.title} (${game.externalId ?? "unknown id"})`);
};

run().catch((error) => {
  console.error("Failed to fetch current PSN game.");
  console.error(error);
  process.exit(1);
});
