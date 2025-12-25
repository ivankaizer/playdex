type LogLevel = "info" | "warn" | "error";

type MetricsSnapshot = {
  pollSuccessTotal: number;
  pollFailureTotal: number;
  pollSuccessByPlatform: Record<string, number>;
  pollFailureByPlatform: Record<string, number>;
  lastPollSuccessAt: string | null;
  lastPollFailureAt: string | null;
  lastCycleDurationMs: number | null;
};

const metrics: MetricsSnapshot = {
  pollSuccessTotal: 0,
  pollFailureTotal: 0,
  pollSuccessByPlatform: {},
  pollFailureByPlatform: {},
  lastPollSuccessAt: null,
  lastPollFailureAt: null,
  lastCycleDurationMs: null,
};

const bump = (bucket: Record<string, number>, key: string) => {
  bucket[key] = (bucket[key] ?? 0) + 1;
};

export const logEvent = (
  level: LogLevel,
  event: string,
  fields: Record<string, unknown> = {}
) => {
  const payload = {
    level,
    event,
    timestamp: new Date().toISOString(),
    ...fields,
  };
  const line = JSON.stringify(payload);
  if (level === "error") {
    console.error(line);
  } else if (level === "warn") {
    console.warn(line);
  } else {
    console.log(line);
  }
};

export const getErrorMessage = (error: unknown) => {
  if (error instanceof Error) {
    return error.message;
  }
  return typeof error === "string" ? error : "Unknown error";
};

export const recordPollSuccess = (platform: string) => {
  metrics.pollSuccessTotal += 1;
  metrics.lastPollSuccessAt = new Date().toISOString();
  bump(metrics.pollSuccessByPlatform, platform);
};

export const recordPollFailure = (platform: string) => {
  metrics.pollFailureTotal += 1;
  metrics.lastPollFailureAt = new Date().toISOString();
  bump(metrics.pollFailureByPlatform, platform);
};

export const recordPollCycle = (durationMs: number) => {
  metrics.lastCycleDurationMs = durationMs;
};

export const getMetrics = (): MetricsSnapshot => ({
  pollSuccessTotal: metrics.pollSuccessTotal,
  pollFailureTotal: metrics.pollFailureTotal,
  pollSuccessByPlatform: { ...metrics.pollSuccessByPlatform },
  pollFailureByPlatform: { ...metrics.pollFailureByPlatform },
  lastPollSuccessAt: metrics.lastPollSuccessAt,
  lastPollFailureAt: metrics.lastPollFailureAt,
  lastCycleDurationMs: metrics.lastCycleDurationMs,
});
