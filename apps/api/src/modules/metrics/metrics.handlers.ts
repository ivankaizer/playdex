import { requireAuth } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type { ApiResponse, MetricsResponseDto } from "../../app/http/types";
import { metricsService } from "./metrics.service";

export const metricsHandler = async (ctx: {
  request: Request;
}): Promise<ApiResponse<MetricsResponseDto>> => {
  requireAuth(ctx.request);
  const response: MetricsResponseDto = metricsService.getSnapshot();
  return ok(response);
};
