import { Elysia } from "elysia";

import { getSql } from "./db";
import { startPolling } from "./polling";
import type { PlatformAccount } from "./services";
import { PsnClient, SteamClient } from "./services";

const DEFAULT_POLL_INTERVAL_SECONDS = 60;
const MIN_POLL_INTERVAL_SECONDS = 10;
const MAX_POLL_INTERVAL_SECONDS = 3600;

const requiredEnv = [
  "DATABASE_URL",
  "PSN_NPSSO",
  "STEAM_API_KEY",
  "STEAM_ID",
] as const;

const missingEnv = requiredEnv.filter((key) => !Bun.env[key]?.trim());
if (missingEnv.length > 0) {
  console.error(
    `Missing required environment variables: ${missingEnv.join(", ")}`
  );
  process.exit(1);
}

const resolvePollIntervalSeconds = (raw: string | undefined) => {
  if (!raw) {
    return DEFAULT_POLL_INTERVAL_SECONDS;
  }

  const parsed = Number.parseInt(raw, 10);
  if (Number.isNaN(parsed)) {
    throw new Error("POLL_INTERVAL_SECONDS must be a number.");
  }

  const clamped = Math.min(
    Math.max(parsed, MIN_POLL_INTERVAL_SECONDS),
    MAX_POLL_INTERVAL_SECONDS
  );

  if (clamped !== parsed) {
    console.warn(
      `POLL_INTERVAL_SECONDS was clamped to ${clamped} (min ${MIN_POLL_INTERVAL_SECONDS}, max ${MAX_POLL_INTERVAL_SECONDS}).`
    );
  }

  return clamped;
};

const config = {
  databaseUrl: Bun.env.DATABASE_URL ?? "",
  pollIntervalSeconds: resolvePollIntervalSeconds(
    Bun.env.POLL_INTERVAL_SECONDS
  ),
  psnNpsso: Bun.env.PSN_NPSSO ?? "",
  psnAccountId: Bun.env.PSN_ACCOUNT_ID ?? "",
  steamApiKey: Bun.env.STEAM_API_KEY ?? "",
  steamId: Bun.env.STEAM_ID ?? "",
};

const app = new Elysia()
  .get("/", () => "Hello Elysia")
  .get("/db/health", async () => {
    const sql = getSql();
    await sql`select 1`;
    return { ok: true };
  })
  .listen(3000);

console.log(
  `🦊 Elysia is running at ${app.server?.hostname}:${app.server?.port}`
);
console.log(`Polling interval: ${config.pollIntervalSeconds}s`);

const bootstrapPolling = async () => {
  const accounts: PlatformAccount[] = [];
  const steamClient = new SteamClient({
    apiKey: config.steamApiKey,
    steamId: config.steamId,
  });
  const psnClient = new PsnClient({ npsso: config.psnNpsso });

  if (config.steamId.trim()) {
    accounts.push({
      platform: "steam",
      accountId: config.steamId,
    });
  }

  let psnAccountId = config.psnAccountId.trim();
  if (!psnAccountId) {
    const resolved = await psnClient.resolveAccountId();
    if (resolved) {
      psnAccountId = resolved;
      console.log(`Resolved PSN account ID: ${psnAccountId}`);
    } else {
      console.warn("Could not resolve PSN account ID; skipping PSN polling.");
    }
  }

  if (psnAccountId) {
    accounts.push({
      platform: "psn",
      accountId: psnAccountId,
    });
  }

  await startPolling({
    sql: getSql(),
    clients: [steamClient, psnClient],
    accounts,
    pollIntervalSeconds: config.pollIntervalSeconds,
  });
};

bootstrapPolling().catch((error) => {
  console.error("Failed to start polling.");
  console.error(error);
});
