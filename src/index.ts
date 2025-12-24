import { Elysia } from "elysia";

import { getSql } from "./db";
import { startPolling } from "./polling";
import type { PlatformAccount, PlatformClient } from "./services";
import { PsnClient, SteamClient } from "./services";

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

type PlatformCredentialRow = {
  id: number;
  platform: PlatformAccount["platform"];
  api_key: string | null;
  account_id: string | null;
};

const loadPlatformCredentials = async () => {
  const sql = getSql();
  const rows = await sql<PlatformCredentialRow[]>`
    select id, platform, api_key, account_id
    from platform_credentials
  `;
  return new Map(rows.map((row) => [row.platform, row]));
};

const storeResolvedAccountId = async (
  platform: PlatformAccount["platform"],
  accountId: string
) => {
  const sql = getSql();
  await sql`
    update platform_credentials
    set account_id = ${accountId}, updated_at = now()
    where platform = ${platform}
  `;
};

const app = new Elysia()
  .get("/", () => "Hello Elysia")
  .get("/db/health", async () => {
    const sql = getSql();
    await sql`select 1`;
    return { ok: true };
  })
  .get("/accounts", async () => {
    const sql = getSql();
    const rows = await sql<{ id: number; platform: string }[]>`
      select id, platform
      from platform_credentials
      order by id
    `;
    return rows.map((r) => ({ id: +r.id, name: r.platform }));
  })
  .get("/accounts/:accountId/sessions", async ({ params, set }) => {
    const accountId = Number(params.accountId);
    if (!Number.isInteger(accountId)) {
      set.status = 400;
      return { error: "accountId must be a number." };
    }

    const sql = getSql();
    const rows = await sql<
      Array<{
        id: number;
        started_at: Date;
        ended_at: Date | null;
        platform: string;
        title: string;
      }>
    >`
      select
        ps.id,
        ps.started_at,
        ps.ended_at,
        pc.platform,
        g.title
      from play_sessions ps
      join platform_credentials pc on pc.id = ps.platform_credential_id
      join games g on g.id = ps.game_id
      where ps.platform_credential_id = ${accountId}
      order by
        case when ps.ended_at is null then 0 else 1 end,
        ps.started_at desc
      limit 10
    `;

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

const bootstrapPolling = async () => {
  const accounts: Array<PlatformAccount & { credentialId: number }> = [];
  const clients: PlatformClient[] = [];
  const credentials = await loadPlatformCredentials();

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

bootstrapPolling().catch((error) => {
  console.error("Failed to start polling.");
  console.error(error);
});
