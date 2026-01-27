import { normalizeGame } from "../games";
import type {
  PlatformAccount,
  PlatformClient,
} from "../../common/domain/platform";
import {
  getErrorMessage,
  logEvent,
  recordPollCycle,
  recordPollFailure,
  recordPollSuccess,
} from "../../common/observability";
import {
  pollingRepository,
  type SqlClient,
} from "./polling.repository";

type PollingConfig = {
  sql: SqlClient;
  loadAccounts: () => Promise<PollingAccountEntry[]>;
  pollIntervalSeconds: number;
  maxCycles?: number;
  notifier?: PollingNotifier;
  onCycleComplete?: (summary: {
    durationMs: number;
    accountCount: number;
  }) => void;
};

export type PollingAccountEntry = {
  platformCredentialId: number;
  account: PlatformAccount;
  client: PlatformClient;
};

export type PollingNotifier = {
  onPollFailure?: (payload: {
    platformCredentialId: number;
    platform: string;
    message: string;
    backoffMs: number;
  }) => Promise<void>;
  onSessionStarted?: (payload: {
    platformCredentialId: number;
    gameId: number;
    startedAt: Date;
  }) => Promise<void>;
  onSessionEnded?: (payload: {
    platformCredentialId: number;
    gameId: number;
    startedAt: Date;
    endedAt: Date;
    durationSeconds: number;
  }) => Promise<void>;
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

const safeNotify = async <T>(
  handler: ((payload: T) => Promise<void>) | undefined,
  payload: T,
  eventName: string
) => {
  if (!handler) {
    return;
  }

  try {
    await handler(payload);
  } catch (error) {
    logEvent("error", "poll.notification.failed", {
      event: eventName,
      message: getErrorMessage(error),
    });
  }
};

export const syncPlaySession = async (
  sql: SqlClient,
  platformCredentialId: number,
  gameId: number | null,
  polledAt: Date,
  notifier?: PollingNotifier
) => {
  const activeSession = await pollingRepository.fetchActivePlaySession(
    platformCredentialId,
    sql
  );
  if (!gameId) {
    if (activeSession) {
      await pollingRepository.endPlaySession(activeSession.id, polledAt, sql);
      logEvent("info", "db.play_session.ended", {
        sessionId: activeSession.id,
        platformCredentialId,
        endedAt: polledAt.toISOString(),
      });
      const durationSeconds = Math.max(
        0,
        Math.round((polledAt.getTime() - activeSession.started_at.getTime()) / 1000)
      );
      await safeNotify(
        notifier?.onSessionEnded,
        {
          platformCredentialId,
          gameId: activeSession.game_id,
          startedAt: activeSession.started_at,
          endedAt: polledAt,
          durationSeconds,
        },
        "session.ended"
      );
    }
    return;
  }

  if (!activeSession) {
    await pollingRepository.startPlaySession(
      platformCredentialId,
      gameId,
      polledAt,
      sql
    );
    logEvent("info", "db.play_session.started", {
      platformCredentialId,
      gameId,
      startedAt: polledAt.toISOString(),
    });
    await safeNotify(
      notifier?.onSessionStarted,
      { platformCredentialId, gameId, startedAt: polledAt },
      "session.started"
    );
    return;
  }

  if (activeSession.game_id === gameId) {
    return;
  }

  await pollingRepository.endPlaySession(activeSession.id, polledAt, sql);
  logEvent("info", "db.play_session.ended", {
    sessionId: activeSession.id,
    platformCredentialId,
    endedAt: polledAt.toISOString(),
  });
  const durationSeconds = Math.max(
    0,
    Math.round((polledAt.getTime() - activeSession.started_at.getTime()) / 1000)
  );
  await safeNotify(
    notifier?.onSessionEnded,
    {
      platformCredentialId,
      gameId: activeSession.game_id,
      startedAt: activeSession.started_at,
      endedAt: polledAt,
      durationSeconds,
    },
    "session.ended"
  );
  await pollingRepository.startPlaySession(
    platformCredentialId,
    gameId,
    polledAt,
    sql
  );
  logEvent("info", "db.play_session.started", {
    platformCredentialId,
    gameId,
    startedAt: polledAt.toISOString(),
  });
  await safeNotify(
    notifier?.onSessionStarted,
    { platformCredentialId, gameId, startedAt: polledAt },
    "session.started"
  );
};

export const startPolling = async ({
  sql,
  loadAccounts,
  pollIntervalSeconds,
  maxCycles,
  notifier,
  onCycleComplete,
}: PollingConfig) => {
  const backoffState = new Map<number, PlatformBackoffState>();
  let warnedNoAccounts = false;
  let cycleCount = 0;

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
      const state = backoffState.get(entry.platformCredentialId) ?? {
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
        let ignored = false;

        if (result.game) {
          const normalized = normalizeGame(result.game);
          gameId = await pollingRepository.ensureGameId(normalized, sql);
          ignored = await pollingRepository.isGameIgnoredForCredential(
            entry.platformCredentialId,
            gameId,
            sql
          );
          if (ignored) {
            logEvent("info", "poll.game.ignored", {
              platform: entry.account.platform,
              platformCredentialId: entry.platformCredentialId,
              gameId,
              canonicalTitle: normalized.canonicalTitle,
              title: normalized.title,
            });
          }
        }

        await pollingRepository.recordPollResult(
          entry.platformCredentialId,
          gameId,
          polledAt,
          result.raw,
          sql
        );
        logEvent("info", "db.poll_result.inserted", {
          platformCredentialId: entry.platformCredentialId,
          gameId: gameId ?? null,
          polledAt: polledAt.toISOString(),
        });
        const sessionGameId = ignored ? null : gameId;
        await syncPlaySession(
          sql,
          entry.platformCredentialId,
          sessionGameId,
          polledAt,
          ignored ? undefined : notifier
        );

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
        await safeNotify(
          notifier?.onPollFailure,
          {
            platformCredentialId: entry.platformCredentialId,
            platform: entry.account.platform,
            message: getErrorMessage(error),
            backoffMs: delayMs,
          },
          "poll.failure"
        );
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
    cycleCount += 1;
    onCycleComplete?.({ durationMs: elapsedMs, accountCount: accounts.length });
    if (maxCycles && cycleCount >= maxCycles) {
      return;
    }
    const waitMs = Math.max(pollIntervalSeconds * 1000 - elapsedMs, 0);
    setTimeout(() => {
      void pollOnce();
    }, waitMs);
  };

  void pollOnce();
};
