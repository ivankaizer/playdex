import { getMetrics } from "../../common/observability";

export const metricsService = {
  getSnapshot: () => getMetrics(),
};
