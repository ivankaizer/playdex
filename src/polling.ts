import type postgres from "postgres";

import { normalizeGame } from "./services/normalize";
import type {
  Platform,
  PlatformAccount,
  PlatformClient,
} from "./services/platform";

type PollingConfig = {
  sql: ReturnType<typeof postgres>;
  clients: PlatformClient[];
  accounts: PlatformAccount[];
  pollIntervalSeconds: number;
};

type PollingAccount = {
  platformAccountId: number;
  account: PlatformAccount;
  client: PlatformClient;
};

type PlatformBackoffState = {
  failures: number;
  nextAllowedAt: number;
  lastError?: string;
};

const DEFAULT_ACCOUNT_NAME = "primary";
const BACKOFF_BASE_MS = 5_000;
const BACKOFF_MAX_MS = 10 * 60_000;
const RATE_LIMIT_FALLBACK_MS = 60_000;

const computeBackoffMs = (failures: number) =>
  Math.min(BACKOFF_BASE_MS * 2 ** Math.max(failures - 1, 0), BACKOFF_MAX_MS);

const extractRetryAfterMs = (error: unknown): number | null => {
  if (!error || typeof error !== "object") {
    return null;
  }

  const withRetryAfter = error as { retryAfterMs?: number };
  if (typeof withRetryAfter.retryAfterMs === "number") {
    return withRetryAfter.retryAfterMs;
  }

  const withStatus = error as { status?: number; statusCode?: number };
  const status = withStatus.status ?? withStatus.statusCode;
  if (status === 429) {
    return RATE_LIMIT_FALLBACK_MS;
  }

  return null;
};

const getErrorMessage = (error: unknown) => {
  if (error instanceof Error) {
    return error.message;
  }
  return typeof error === "string" ? error : "Unknown error";
};

const ensureAccountId = async (
  sql: ReturnType<typeof postgres>,
  displayName: string
) => {
  const existing = await sql<{ id: number }[]>`
    select id from accounts where display_name = ${displayName} limit 1
  `;
  if (existing.length > 0) {
    return existing[0].id;
  }

  const inserted = await sql<{ id: number }[]>`
    insert into accounts (display_name)
    values (${displayName})
    returning id
  `;
  return inserted[0].id;
};

const ensurePlatformAccountId = async (
  sql: ReturnType<typeof postgres>,
  accountId: number,
  account: PlatformAccount
) => {
  const rows = await sql<{ id: number }[]>`
    insert into platform_accounts (account_id, platform, external_id, display_name)
    values (${accountId}, ${account.platform}, ${account.accountId}, ${account.displayName ?? null})
    on conflict (platform, external_id)
    do update set
      account_id = excluded.account_id,
      display_name = excluded.display_name,
      updated_at = now()
    returning id
  `;
  return rows[0].id;
};

const ensureGameId = async (
  sql: ReturnType<typeof postgres>,
  game: ReturnType<typeof normalizeGame>
) => {
  const rows = await sql<{ id: number }[]>`
    insert into games (platform, title, external_id, canonical_key)
    values (${game.platform}, ${game.title}, ${game.externalId ?? null}, ${game.canonicalKey})
    on conflict (canonical_key)
    do update set
      title = excluded.title,
      external_id = excluded.external_id,
      updated_at = now()
    returning id
  `;
  return rows[0].id;
};

const recordPollResult = async (
  sql: ReturnType<typeof postgres>,
  platformAccountId: number,
  gameId: number | null,
  polledAt: Date,
  rawPayload: unknown
) => {
  const payload = rawPayload === undefined ? null : sql.json(rawPayload);
  await sql`
    insert into poll_results (platform_account_id, game_id, polled_at, raw_payload)
    values (${platformAccountId}, ${gameId}, ${polledAt}, ${payload})
  `;
};

const syncPlaySession = async (
  sql: ReturnType<typeof postgres>,
  platformAccountId: number,
  gameId: number | null,
  polledAt: Date
) => {
  const activeRows = await sql<{ id: number; game_id: number }[]>`
    select id, game_id
    from play_sessions
    where platform_account_id = ${platformAccountId}
      and ended_at is null
    order by started_at desc
    limit 1
  `;

  const activeSession = activeRows[0];
  if (!gameId) {
    if (activeSession) {
      await sql`
        update play_sessions
        set ended_at = ${polledAt}, updated_at = now()
        where id = ${activeSession.id}
      `;
    }
    return;
  }

  if (!activeSession) {
    await sql`
      insert into play_sessions (platform_account_id, game_id, started_at)
      values (${platformAccountId}, ${gameId}, ${polledAt})
    `;
    return;
  }

  if (activeSession.game_id === gameId) {
    return;
  }

  await sql`
    update play_sessions
    set ended_at = ${polledAt}, updated_at = now()
    where id = ${activeSession.id}
  `;
  await sql`
    insert into play_sessions (platform_account_id, game_id, started_at)
    values (${platformAccountId}, ${gameId}, ${polledAt})
  `;
};

export const startPolling = async ({
  sql,
  clients,
  accounts,
  pollIntervalSeconds,
}: PollingConfig) => {
  if (accounts.length === 0) {
    console.warn("No configured platform accounts; polling is disabled.");
    return;
  }

  const clientByPlatform = new Map<Platform, PlatformClient>(
    clients.map((client) => [client.platform, client])
  );
  const rootAccountId = await ensureAccountId(sql, DEFAULT_ACCOUNT_NAME);
  const pollingAccounts: PollingAccount[] = [];

  for (const account of accounts) {
    const client = clientByPlatform.get(account.platform);
    if (!client) {
      console.warn(`Missing client for platform ${account.platform}.`);
      continue;
    }

    const platformAccountId = await ensurePlatformAccountId(
      sql,
      rootAccountId,
      account
    );

    pollingAccounts.push({
      platformAccountId,
      account,
      client,
    });
  }

  if (pollingAccounts.length === 0) {
    console.warn("No polling accounts were initialized.");
    return;
  }

  const backoffState = new Map<Platform, PlatformBackoffState>();

  const pollOnce = async () => {
    const cycleStartedAt = Date.now();
    for (const entry of pollingAccounts) {
      const now = Date.now();
      const state =
        backoffState.get(entry.account.platform) ?? { failures: 0, nextAllowedAt: 0 };

      if (state.nextAllowedAt > now) {
        console.warn(
          `Skipping ${entry.account.platform} until ${new Date(state.nextAllowedAt).toISOString()} (${state.lastError ?? "rate limit/backoff"}).`
        );
        backoffState.set(entry.account.platform, state);
        continue;
      }

      try {
        const result = await entry.client.getCurrentlyPlaying(entry.account);
        const polledAt = new Date();
        let gameId: number | null = null;

        if (result.game) {
          const normalized = normalizeGame(result.game);
          gameId = await ensureGameId(sql, normalized);
        }

        await recordPollResult(
          sql,
          entry.platformAccountId,
          gameId,
          polledAt,
          result.raw
        );
        await syncPlaySession(sql, entry.platformAccountId, gameId, polledAt);

        backoffState.set(entry.account.platform, {
          failures: 0,
          nextAllowedAt: 0,
        });
      } catch (error) {
        const previous = backoffState.get(entry.account.platform) ?? {
          failures: 0,
          nextAllowedAt: 0,
        };
        const failures = previous.failures + 1;
        const backoffMs = computeBackoffMs(failures);
        const retryAfterMs = extractRetryAfterMs(error);
        const delayMs = Math.max(backoffMs, retryAfterMs ?? 0);
        const nextAllowedAt = now + delayMs;
        backoffState.set(entry.account.platform, {
          failures,
          nextAllowedAt,
          lastError: getErrorMessage(error),
        });

        console.error(
          `Polling failed for ${entry.account.platform}: ${getErrorMessage(error)}`
        );
      }
    }

    const elapsedMs = Date.now() - cycleStartedAt;
    const waitMs = Math.max(pollIntervalSeconds * 1000 - elapsedMs, 0);
    setTimeout(() => {
      void pollOnce();
    }, waitMs);
  };

  void pollOnce();
};
