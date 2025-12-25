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
import {
  getErrorMessage,
  logEvent,
  recordPollCycle,
  recordPollFailure,
  recordPollSuccess,
} from "./observability";

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
      logEvent("info", "db.play_session.ended", {
        sessionId: activeSession.id,
        platformCredentialId,
        endedAt: polledAt.toISOString(),
      });
    }
    return;
  }

  if (!activeSession) {
    await startPlaySession(platformCredentialId, gameId, polledAt, sql);
    logEvent("info", "db.play_session.started", {
      platformCredentialId,
      gameId,
      startedAt: polledAt.toISOString(),
    });
    return;
  }

  if (activeSession.game_id === gameId) {
    return;
  }

  await endPlaySession(activeSession.id, polledAt, sql);
  logEvent("info", "db.play_session.ended", {
    sessionId: activeSession.id,
    platformCredentialId,
    endedAt: polledAt.toISOString(),
  });
  await startPlaySession(platformCredentialId, gameId, polledAt, sql);
  logEvent("info", "db.play_session.started", {
    platformCredentialId,
    gameId,
    startedAt: polledAt.toISOString(),
  });
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
    logEvent("info", "poll.cycle.started", {
      pollIntervalSeconds,
    });
    let accounts: PollingAccountEntry[] = [];
    try {
      accounts = await loadAccounts();
    } catch (error) {
      logEvent("error", "poll.load_accounts.failed", {
        message: getErrorMessage(error),
      });
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
        logEvent("info", "db.poll_result.inserted", {
          platformCredentialId: entry.platformCredentialId,
          gameId,
          polledAt: polledAt.toISOString(),
        });
        await syncPlaySession(sql, entry.platformCredentialId, gameId, polledAt);

        backoffState.set(entry.platformCredentialId, {
          failures: 0,
          nextAllowedAt: 0,
        });
        recordPollSuccess(entry.account.platform);
        logEvent("info", "poll.account.success", {
          platform: entry.account.platform,
          platformCredentialId: entry.platformCredentialId,
          gameId,
          polledAt: polledAt.toISOString(),
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

        recordPollFailure(entry.account.platform);
        logEvent("error", "poll.account.failure", {
          platform: entry.account.platform,
          platformCredentialId: entry.platformCredentialId,
          message: getErrorMessage(error),
          backoffMs: delayMs,
          nextAllowedAt: new Date(nextAllowedAt).toISOString(),
        });
      }
    }

    const elapsedMs = Date.now() - cycleStartedAt;
    recordPollCycle(elapsedMs);
    logEvent("info", "poll.cycle.completed", {
      durationMs: elapsedMs,
      accountCount: accounts.length,
    });
    const waitMs = Math.max(pollIntervalSeconds * 1000 - elapsedMs, 0);
    setTimeout(() => {
      void pollOnce();
    }, waitMs);
  };

  void pollOnce();
};
