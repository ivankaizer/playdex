import type postgres from "postgres";

import { normalizeGame } from "./services/normalize";
import type { PlatformAccount, PlatformClient } from "./services/platform";
import {
  endPlaySession,
  ensureGameId,
  fetchActivePlaySession,
  recordPollResult,
  startPlaySession,
} from "./sql/queries";

type PollingConfig = {
  sql: ReturnType<typeof postgres>;
  loadAccounts: () => Promise<PollingAccountEntry[]>;
  pollIntervalSeconds: number;
};

export type PollingAccountEntry = {
  platformCredentialId: number;
  account: PlatformAccount;
  client: PlatformClient;
};

type PlatformBackoffState = {
  failures: number;
  nextAllowedAt: number;
  lastError?: string;
};

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

const syncPlaySession = async (
  sql: ReturnType<typeof postgres>,
  platformCredentialId: number,
  gameId: number | null,
  polledAt: Date
) => {
  const activeSession = await fetchActivePlaySession(
    platformCredentialId,
    sql
  );
  if (!gameId) {
    if (activeSession) {
      await endPlaySession(activeSession.id, polledAt, sql);
    }
    return;
  }

  if (!activeSession) {
    await startPlaySession(platformCredentialId, gameId, polledAt, sql);
    return;
  }

  if (activeSession.game_id === gameId) {
    return;
  }

  await endPlaySession(activeSession.id, polledAt, sql);
  await startPlaySession(platformCredentialId, gameId, polledAt, sql);
};

export const startPolling = async ({
  sql,
  loadAccounts,
  pollIntervalSeconds,
}: PollingConfig) => {
  const backoffState = new Map<number, PlatformBackoffState>();
  let warnedNoAccounts = false;

  const pollOnce = async () => {
    const cycleStartedAt = Date.now();
    let accounts: PollingAccountEntry[] = [];
    try {
      accounts = await loadAccounts();
    } catch (error) {
      console.error("Failed to load platform credentials.");
      console.error(error);
    }

    if (accounts.length === 0) {
      if (!warnedNoAccounts) {
        console.warn(
          "No configured platform credentials; polling will keep checking for updates."
        );
        warnedNoAccounts = true;
      }
    } else {
      warnedNoAccounts = false;
    }

    const activeIds = new Set(
      accounts.map((entry) => entry.platformCredentialId)
    );
    for (const id of backoffState.keys()) {
      if (!activeIds.has(id)) {
        backoffState.delete(id);
      }
    }

    for (const entry of accounts) {
      const now = Date.now();
      const state =
        backoffState.get(entry.platformCredentialId) ?? {
          failures: 0,
          nextAllowedAt: 0,
        };

      if (state.nextAllowedAt > now) {
        console.warn(
          `Skipping ${entry.account.platform}(${entry.platformCredentialId}) until ${new Date(state.nextAllowedAt).toISOString()} (${state.lastError ?? "rate limit/backoff"}).`
        );
        backoffState.set(entry.platformCredentialId, state);
        continue;
      }

      try {
        const result = await entry.client.getCurrentlyPlaying(entry.account);
        const polledAt = new Date();
        let gameId: number | null = null;

        if (result.game) {
          const normalized = normalizeGame(result.game);
          gameId = await ensureGameId(normalized, sql);
        }

        await recordPollResult(
          entry.platformCredentialId,
          gameId,
          polledAt,
          result.raw,
          sql
        );
        await syncPlaySession(sql, entry.platformCredentialId, gameId, polledAt);

        backoffState.set(entry.platformCredentialId, {
          failures: 0,
          nextAllowedAt: 0,
        });
      } catch (error) {
        const previous = backoffState.get(entry.platformCredentialId) ?? {
          failures: 0,
          nextAllowedAt: 0,
        };
        const failures = previous.failures + 1;
        const backoffMs = computeBackoffMs(failures);
        const retryAfterMs = extractRetryAfterMs(error);
        const delayMs = Math.max(backoffMs, retryAfterMs ?? 0);
        const nextAllowedAt = now + delayMs;
        backoffState.set(entry.platformCredentialId, {
          failures,
          nextAllowedAt,
          lastError: getErrorMessage(error),
        });

        console.error(
          `Polling failed for ${entry.account.platform}(${entry.platformCredentialId}): ${getErrorMessage(error)}`
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
