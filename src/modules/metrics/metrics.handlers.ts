import { ok } from "../../app/http/responses";
import type { ApiResponse, MetricsResponseDto } from "../../app/http/types";
import { metricsService } from "./metrics.service";

export const metricsHandler = async (): Promise<
  ApiResponse<MetricsResponseDto>
> => {
  const response: MetricsResponseDto = metricsService.getSnapshot();
  return ok(response);
};

