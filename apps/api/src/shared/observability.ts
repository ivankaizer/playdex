// Compatibility re-export. Prefer importing from src/common/observability.
export {
  getErrorMessage,
  getMetrics,
  logEvent,
  recordPollCycle,
  recordPollFailure,
  recordPollSuccess,
  type MetricsSnapshot,
} from "../common/observability";
