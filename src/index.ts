import { Elysia } from "elysia";

import { getSql } from "./db";
import { runMigrations } from "./migrations";
import { startPolling } from "./polling";
import type { PlatformAccount, PlatformClient } from "./services";
import { PsnClient, SteamClient } from "./services";
import {
  healthCheck,
  listAccountSessions,
  listAccounts,
  loadPlatformCredentials,
  storeResolvedAccountId as storeResolvedAccountIdQuery,
} from "./sql/queries";

const DEFAULT_POLL_INTERVAL_SECONDS = 60;
const MIN_POLL_INTERVAL_SECONDS = 10;
const MAX_POLL_INTERVAL_SECONDS = 3600;

const requiredEnv = ["DATABASE_URL"] as const;

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
};

const loadCredentialsByPlatform = async () => {
  const rows = await loadPlatformCredentials();
  return new Map(rows.map((row) => [row.platform, row]));
};

const storeResolvedAccountId = async (
  platform: PlatformAccount["platform"],
  accountId: string
) => {
  await storeResolvedAccountIdQuery(platform, accountId);
};

const bootstrapPolling = async () => {
  const accounts: Array<PlatformAccount & { credentialId: number }> = [];
  const clients: PlatformClient[] = [];
  const credentials = await loadCredentialsByPlatform();

  const steamCredentials = credentials.get("steam");
  if (
    steamCredentials?.api_key?.trim() &&
    steamCredentials.account_id?.trim()
  ) {
    const steamClient = new SteamClient({
      apiKey: steamCredentials.api_key,
      steamId: steamCredentials.account_id,
    });
    clients.push(steamClient);
    accounts.push({
      platform: "steam",
      accountId: steamCredentials.account_id,
      credentialId: steamCredentials.id,
    });
  } else if (steamCredentials) {
    console.warn(
      "Missing Steam credentials or account ID; skipping Steam polling."
    );
  }

  const psnCredentials = credentials.get("psn");
  if (psnCredentials?.api_key?.trim()) {
    const psnClient = new PsnClient({ npsso: psnCredentials.api_key });
    clients.push(psnClient);
    let psnAccountId = psnCredentials.account_id?.trim() ?? "";
    if (!psnAccountId) {
      const resolved = await psnClient.resolveAccountId();
      if (resolved) {
        psnAccountId = resolved;
        console.log(`Resolved PSN account ID: ${psnAccountId}`);
        await storeResolvedAccountId("psn", psnAccountId);
      } else {
        console.warn("Could not resolve PSN account ID; skipping PSN polling.");
      }
    }

    if (psnAccountId) {
      accounts.push({
        platform: "psn",
        accountId: psnAccountId,
        credentialId: psnCredentials.id,
      });
    }
  } else if (psnCredentials) {
    console.warn("Missing PSN credentials; skipping PSN polling.");
  }

  await startPolling({
    sql: getSql(),
    clients,
    accounts,
    pollIntervalSeconds: config.pollIntervalSeconds,
  });
};

const startServer = async () => {
  await runMigrations();

  const app = new Elysia()
    .get("/", () => "Hello Elysia")
    .get("/db/health", async () => {
      await healthCheck();
      return { ok: true };
    })
    .get("/accounts", async () => {
      const rows = await listAccounts();
      return rows.map((r) => ({ id: +r.id, name: r.platform }));
    })
    .get("/accounts/:accountId/sessions", async ({ params, set }) => {
      const accountId = Number(params.accountId);
      if (!Number.isInteger(accountId)) {
        set.status = 400;
        return { error: "accountId must be a number." };
      }

      const rows = await listAccountSessions(accountId);

      return {
        accountId,
        sessions: rows.map((r) => ({
          sessionId: +r.id,
          startedAt: r.started_at,
          endedAt: r.ended_at,
          platform: r.platform,
          game: r.title,
        })),
      };
    })
    .listen(3000);

  console.log(
    `🦊 Elysia is running at ${app.server?.hostname}:${app.server?.port}`
  );
  console.log(`Polling interval: ${config.pollIntervalSeconds}s`);

  await bootstrapPolling();
};

startServer().catch((error) => {
  console.error("Startup failed.");
  console.error(error);
});
