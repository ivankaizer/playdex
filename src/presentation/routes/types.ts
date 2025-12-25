import type { MetricsSnapshot } from "../../shared/observability";

export type ApiErrorResponse = {
  error: string;
};

export type ApiResponse<T> =
  | { data: T; error: null }
  | { data: null; error: ApiErrorResponse };

export type HealthResponseDto = {
  ok: true;
};

export type AccountSummaryDto = {
  id: number;
  name: string;
};

export type AccountSessionDto = {
  sessionId: number;
  startedAt: Date;
  endedAt: Date | null;
  platform: string;
  game: string;
};

export type AccountSessionsResponseDto = {
  accountId: number;
  sessions: AccountSessionDto[];
};

export type MetricsResponseDto = MetricsSnapshot;

export type AccountSessionsParams = {
  accountId: string;
};
