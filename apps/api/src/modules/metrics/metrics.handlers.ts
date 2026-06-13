import type { AuthenticatedContext } from "../../app/http/auth";
import { ok } from "../../app/http/responses";
import type { ApiResponse, MetricsResponseDto } from "../../app/http/types";
import { metricsService } from "./metrics.service";

export const metricsHandler = async (
  ctx: AuthenticatedContext
): Promise<ApiResponse<MetricsResponseDto>> => {
  void ctx.auth;
  const response: MetricsResponseDto = metricsService.getSnapshot();
  return ok(response);
};
